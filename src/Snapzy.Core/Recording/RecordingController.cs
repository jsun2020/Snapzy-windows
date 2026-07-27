using System.Diagnostics;

namespace Snapzy.Core.Recording;

public enum RecordingState { Idle, Recording, Paused }

public class RecordingResult
{
    public string? Mp4Path { get; set; }
    public string? GifPath { get; set; }
    public string? WebpPath { get; set; }
    public string? Error { get; set; }
}

public class RecordingController
{
    private readonly string _ffmpegExe;
    private readonly string _workDir; // captures dir; session files live in .rec/<ts>/
    private readonly Func<ProcessStartInfo, IFfmpegProcess> _factory;

    private RecordingOptions? _options;
    private string _finalBaseName = "";
    private string _sessionDir = "";
    private string _stderrLog = "";
    private readonly List<string> _segments = new();
    private IFfmpegProcess? _current;
    private readonly ISystemAudioRecorder? _audio;

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public TimeSpan FastFailDelay { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan QuitTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan EncodeTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public event Action<string>? OnError;

    public RecordingController(string ffmpegExe, string workDir,
        Func<ProcessStartInfo, IFfmpegProcess>? processFactory = null, ISystemAudioRecorder? audio = null)
    {
        _ffmpegExe = ffmpegExe;
        _workDir = workDir;
        _audio = audio;
        _stderrLog = Path.Combine(Log.FilePath is null ? workDir : Path.GetDirectoryName(Log.FilePath)!,
            $"ffmpeg-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _factory = processFactory ?? (psi => new FfmpegProcess(psi, _stderrLog));
    }

    public async Task<bool> StartAsync(RecordingOptions options, string finalBaseName)
    {
        if (State != RecordingState.Idle) return false;
        options.Width -= options.Width % 2;   // encoders require even dimensions
        options.Height -= options.Height % 2;
        _options = options;
        _finalBaseName = finalBaseName;
        _sessionDir = Path.Combine(_workDir, ".rec", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(_sessionDir);
        _segments.Clear();

        var ok = await SpawnSegmentAsync();
        if (!ok && !options.UseGdigrab)
        {
            Log.Error("ddagrab capture failed quickly; retrying with gdigrab");
            options.UseGdigrab = true;
            ok = await SpawnSegmentAsync();
        }
        if (!ok)
        {
            OnError?.Invoke("ffmpeg could not start recording");
            return false;
        }
        State = RecordingState.Recording;
        return true;
    }

    private async Task<bool> SpawnSegmentAsync()
    {
        var seg = Path.Combine(_sessionDir, $"seg-{_segments.Count}.mp4");
        var psi = BuildPsi(FfmpegArgs.BuildRecordArgs(_options!, seg));
        try
        {
            _current = _factory(psi);
        }
        catch (Exception ex)
        {
            Log.Error("ffmpeg spawn failed", ex);
            return false;
        }
        await Task.Delay(FastFailDelay);
        if (_current.HasExited)
        {
            _current = null;
            return false;
        }
        _segments.Add(seg);
        try { _audio?.StartSegment(Path.ChangeExtension(seg, ".wav")); }
        catch (Exception ex) { Log.Error("system audio start failed", ex); }
        return true;
    }

    public async Task PauseAsync()
    {
        if (State != RecordingState.Recording || _current is null) return;
        await FinishCurrentSegmentAsync();
        State = RecordingState.Paused;
    }

    public Task<bool> ResumeAsync()
    {
        if (State != RecordingState.Paused) return Task.FromResult(false);
        return ResumeCoreAsync();
    }

    private async Task<bool> ResumeCoreAsync()
    {
        var ok = await SpawnSegmentAsync();
        if (ok) State = RecordingState.Recording;
        else OnError?.Invoke("ffmpeg could not resume recording");
        return ok;
    }

    private async Task FinishCurrentSegmentAsync()
    {
        if (_current is null) return;
        _current.WriteQuit();
        await _current.WaitForExitAsync(QuitTimeout);
        _current = null;
        try { _audio?.StopSegment(); }
        catch (Exception ex) { Log.Error("system audio stop failed", ex); }
    }

    /// <summary>output: "mp4" | "gif" | "webp" | "both" (mp4+gif) | "mp4+webp"</summary>
    public async Task<RecordingResult> StopAsync(string output)
    {
        var result = new RecordingResult();
        if (State == RecordingState.Idle)
        {
            result.Error = "not recording";
            return result;
        }
        await FinishCurrentSegmentAsync();
        State = RecordingState.Idle;

        try
        {
            var existing = _segments.Where(File.Exists).ToList();
            if (existing.Count == 0)
            {
                result.Error = "no recorded segments were produced (see ffmpeg log)";
                return result;
            }

            var mp4 = Path.Combine(_workDir, _finalBaseName + ".mp4");
            if (existing.Count == 1)
            {
                File.Move(existing[0], mp4, overwrite: true);
            }
            else
            {
                var listFile = Path.Combine(_sessionDir, "list.txt");
                File.WriteAllLines(listFile, existing.Select(s => $"file '{s.Replace("'", "'\\''")}'"));
                if (!await RunToolAsync(FfmpegArgs.BuildConcatArgs(listFile, mp4), mp4))
                {
                    result.Error = "segment concat failed (see ffmpeg log)";
                    return result;
                }
            }

            // System audio: concat wavs and mux into the video
            var wavs = (_audio?.Segments ?? (IReadOnlyList<string>)Array.Empty<string>()).Where(File.Exists).ToList();
            if (wavs.Count > 0)
            {
                var audioWav = wavs[0];
                if (wavs.Count > 1)
                {
                    var wavList = Path.Combine(_sessionDir, "wavlist.txt");
                    File.WriteAllLines(wavList, wavs.Select(s => $"file '{s.Replace("'", "'\\''")}'"));
                    audioWav = Path.Combine(_sessionDir, "audio.wav");
                    if (!await RunToolAsync(FfmpegArgs.BuildConcatArgs(wavList, audioWav), audioWav))
                        audioWav = "";
                }
                if (!string.IsNullOrEmpty(audioWav))
                {
                    var muxed = Path.Combine(_sessionDir, "muxed.mp4");
                    var hasMic = !string.IsNullOrEmpty(_options?.MicDevice);
                    if (await RunToolAsync(FfmpegArgs.BuildMuxArgs(mp4, audioWav, muxed, hasMic), muxed))
                        File.Move(muxed, mp4, overwrite: true);
                    else
                        Log.Error("audio mux failed; keeping silent video");
                }
            }

            var wantGif = output is "gif" or "both";
            var wantWebp = output is "webp" or "mp4+webp";
            var keepMp4 = output is "mp4" or "both" or "mp4+webp";
            if (wantGif)
            {
                var gif = Path.Combine(_workDir, _finalBaseName + ".gif");
                if (await RunToolAsync(FfmpegArgs.BuildGifArgs(mp4, gif), gif)) result.GifPath = gif;
                else result.Error = "gif encode failed (mp4 kept)";
            }
            if (wantWebp)
            {
                var webp = Path.Combine(_workDir, _finalBaseName + ".webp");
                if (await RunToolAsync(FfmpegArgs.BuildAnimatedWebpArgs(mp4, webp), webp)) result.WebpPath = webp;
                else result.Error = "webp encode failed (mp4 kept)";
            }
            if (!keepMp4 && result.Error is null) { File.Delete(mp4); mp4 = ""; }
            if (!string.IsNullOrEmpty(mp4) && File.Exists(mp4)) result.Mp4Path = mp4;

            if (result.Error is null)
            {
                try { Directory.Delete(_sessionDir, recursive: true); } catch (IOException) { }
            }
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("Recording finalize failed", ex);
            result.Error = ex.Message;
            return result; // session dir kept for recovery
        }
    }

    private async Task<bool> RunToolAsync(List<string> args, string expectedOutput)
    {
        var proc = _factory(BuildPsi(args));
        var exit = await proc.WaitForExitAsync(EncodeTimeout);
        // ffmpeg can exit 0 with an empty container (e.g. -shortest against an
        // empty stream); a header-only file must count as failure.
        var info = new FileInfo(expectedOutput);
        return exit == 0 && info.Exists && info.Length > 1024;
    }

    private ProcessStartInfo BuildPsi(List<string> args)
    {
        var psi = new ProcessStartInfo(_ffmpegExe);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }
}
