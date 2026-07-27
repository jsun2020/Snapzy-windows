using System.Diagnostics;
using Snapzy.Core.Recording;

public class RecordingControllerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("snapzy-rec").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private class FakeProcess : IFfmpegProcess
    {
        public bool QuitCalled;
        public bool HasExited { get; set; }
        public void WriteQuit() { QuitCalled = true; HasExited = true; }
        public Task<int> WaitForExitAsync(TimeSpan timeout) { HasExited = true; return Task.FromResult(0); }
    }

    private class Harness
    {
        public readonly List<(ProcessStartInfo Psi, FakeProcess Proc)> Spawns = new();
        public int FailFirstN; // first N record spawns report immediate exit (ddagrab failure)
        public string? ConcatListText; // captured at concat time (session dir is deleted on success)

        public IFfmpegProcess Factory(ProcessStartInfo psi)
        {
            var proc = new FakeProcess();
            var args = psi.ArgumentList.ToList();
            var output = args[^1];
            var isRecord = args.Contains("lavfi") || args.Contains("gdigrab");
            if (isRecord && FailFirstN > 0)
            {
                FailFirstN--;
                proc.HasExited = true;
            }
            else
            {
                if (args.Contains("concat"))
                    ConcatListText = File.ReadAllText(args[args.IndexOf("-i") + 1]);
                // ffmpeg would produce the output file
                File.WriteAllText(output, "stub");
            }
            Spawns.Add((psi, proc));
            return proc;
        }
    }

    private RecordingController MakeController(Harness h) =>
        new("ffmpeg.exe", _dir, h.Factory) { FastFailDelay = TimeSpan.FromMilliseconds(30) };

    private static RecordingOptions Opts() => new() { OffsetX = 0, OffsetY = 0, Width = 1281, Height = 721, Fps = 30 };

    [Fact]
    public async Task Start_SpawnsDdagrabSegment_AndRoundsDimensionsEven()
    {
        var h = new Harness();
        var c = MakeController(h);
        Assert.True(await c.StartAsync(Opts(), "rec1"));
        Assert.Equal(RecordingState.Recording, c.State);
        Assert.Single(h.Spawns);
        var joined = string.Join(" ", h.Spawns[0].Psi.ArgumentList);
        Assert.Contains("ddagrab", joined);
        Assert.Contains("1280x720", joined); // odd dims rounded down
    }

    [Fact]
    public async Task PauseAndResume_QuitsThenSpawnsSecondSegment()
    {
        var h = new Harness();
        var c = MakeController(h);
        await c.StartAsync(Opts(), "rec1");
        await c.PauseAsync();
        Assert.Equal(RecordingState.Paused, c.State);
        Assert.True(h.Spawns[0].Proc.QuitCalled);
        Assert.True(await c.ResumeAsync());
        Assert.Equal(RecordingState.Recording, c.State);
        Assert.Equal(2, h.Spawns.Count);
        Assert.EndsWith("seg-1.mp4", h.Spawns[1].Psi.ArgumentList[^1]);
    }

    [Fact]
    public async Task Stop_TwoSegments_ConcatsWithListFile()
    {
        var h = new Harness();
        var c = MakeController(h);
        await c.StartAsync(Opts(), "rec1");
        await c.PauseAsync();
        await c.ResumeAsync();
        var result = await c.StopAsync("mp4");
        Assert.Null(result.Error);
        Assert.NotNull(result.Mp4Path);
        Assert.True(File.Exists(result.Mp4Path));
        Assert.Null(result.GifPath);
        Assert.Equal(RecordingState.Idle, c.State);
        Assert.Contains(h.Spawns, s => s.Psi.ArgumentList.Contains("concat"));
        Assert.NotNull(h.ConcatListText);
        Assert.Contains("seg-0.mp4", h.ConcatListText);
        Assert.Contains("seg-1.mp4", h.ConcatListText);
    }

    [Fact]
    public async Task Stop_SingleSegment_MovesWithoutConcat()
    {
        var h = new Harness();
        var c = MakeController(h);
        await c.StartAsync(Opts(), "rec1");
        var result = await c.StopAsync("mp4");
        Assert.Null(result.Error);
        Assert.True(File.Exists(result.Mp4Path));
        Assert.DoesNotContain(h.Spawns, s => s.Psi.ArgumentList.Contains("concat"));
        Assert.False(Directory.Exists(System.IO.Path.Combine(_dir, ".rec")) &&
                     Directory.EnumerateDirectories(System.IO.Path.Combine(_dir, ".rec")).Any());
    }

    [Fact]
    public async Task Start_DdagrabFastFail_RetriesWithGdigrab()
    {
        var h = new Harness { FailFirstN = 1 };
        var c = MakeController(h);
        Assert.True(await c.StartAsync(Opts(), "rec1"));
        Assert.Equal(RecordingState.Recording, c.State);
        Assert.Equal(2, h.Spawns.Count);
        Assert.Contains("gdigrab", string.Join(" ", h.Spawns[1].Psi.ArgumentList));
    }

    private class FakeAudio : ISystemAudioRecorder
    {
        public readonly List<string> Started = new();
        public int StopCalls;
        private readonly List<string> _segments = new();
        public IReadOnlyList<string> Segments => _segments;
        public void StartSegment(string wavPath)
        {
            Started.Add(wavPath);
            _segments.Add(wavPath);
            File.WriteAllBytes(wavPath, new byte[] { 0x52, 0x49, 0x46, 0x46 }); // "RIFF" stub
        }
        public void StopSegment() => StopCalls++;
        public void Dispose() { }
    }

    [Fact]
    public async Task Webp_Output_ProducesWebpAndDeletesMp4()
    {
        var h = new Harness();
        var c = MakeController(h);
        await c.StartAsync(Opts(), "rec1");
        var result = await c.StopAsync("webp");
        Assert.Null(result.Error);
        Assert.Null(result.Mp4Path);
        Assert.NotNull(result.WebpPath);
        Assert.True(File.Exists(result.WebpPath));
        Assert.Contains(h.Spawns, s => string.Join(" ", s.Psi.ArgumentList).Contains("libwebp"));
    }

    [Fact]
    public async Task Mp4PlusWebp_KeepsBoth()
    {
        var h = new Harness();
        var c = MakeController(h);
        await c.StartAsync(Opts(), "rec1");
        var result = await c.StopAsync("mp4+webp");
        Assert.Null(result.Error);
        Assert.NotNull(result.Mp4Path);
        Assert.NotNull(result.WebpPath);
    }

    [Fact]
    public async Task SystemAudio_SegmentsFollowPauseResume_AndMuxRuns()
    {
        var h = new Harness();
        var audio = new FakeAudio();
        var c = new RecordingController("ffmpeg.exe", _dir, h.Factory, audio) { FastFailDelay = TimeSpan.FromMilliseconds(30) };
        await c.StartAsync(Opts(), "rec1");
        await c.PauseAsync();
        await c.ResumeAsync();
        var result = await c.StopAsync("mp4");
        Assert.Null(result.Error);
        Assert.Equal(2, audio.Started.Count);          // one wav per video segment
        Assert.Equal(2, audio.StopCalls);
        Assert.Contains(h.Spawns, s => string.Join(" ", s.Psi.ArgumentList).Contains("-map")
            && string.Join(" ", s.Psi.ArgumentList).Contains("aac"));
        Assert.NotNull(result.Mp4Path);
        Assert.True(File.Exists(result.Mp4Path));
    }

    [Fact]
    public async Task Stop_GifMode_ProducesGifAndDeletesMp4()
    {
        var h = new Harness();
        var c = MakeController(h);
        await c.StartAsync(Opts(), "rec1");
        var result = await c.StopAsync("gif");
        Assert.Null(result.Error);
        Assert.Null(result.Mp4Path);
        Assert.NotNull(result.GifPath);
        Assert.True(File.Exists(result.GifPath));
        Assert.Contains(h.Spawns, s => string.Join(" ", s.Psi.ArgumentList).Contains("palettegen"));
    }
}

public class FfmpegDevicesTests
{
    [Fact]
    public void ParseDshowAudio_ExtractsAudioDeviceNames()
    {
        const string stderr = """
            [dshow @ 000001] "Integrated Camera" (video)
            [dshow @ 000001]   Alternative name "@device_pnp_x"
            [dshow @ 000001] "Microphone Array (Realtek(R) Audio)" (audio)
            [dshow @ 000001]   Alternative name "@device_cm_y"
            [dshow @ 000001] "Line In (USB Audio)" (audio)
            dummy: Immediate exit requested
            """;
        var devices = FfmpegDevices.ParseDshowAudio(stderr);
        Assert.Equal(new[] { "Microphone Array (Realtek(R) Audio)", "Line In (USB Audio)" }, devices);
    }
}
