using NAudio.Wave;

namespace Snapzy.Core.Recording;

/// <summary>Records what the system is playing (WASAPI loopback) into WAV segments.</summary>
public sealed class WasapiLoopbackRecorder : ISystemAudioRecorder
{
    private WasapiLoopbackCapture? _capture;
    private WaveFileWriter? _writer;
    private WasapiOut? _silence;
    private readonly object _lock = new();
    private readonly List<string> _segments = new();

    public IReadOnlyList<string> Segments => _segments;

    public void StartSegment(string wavPath)
    {
        lock (_lock)
        {
            StopCore();
            _capture = new WasapiLoopbackCapture();
            // Loopback only delivers data while SOMETHING is playing; render
            // silence so a quiet system still produces a continuous track.
            _silence = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 200);
            _silence.Init(new SilenceProvider(_capture.WaveFormat));
            _silence.Play();
            _writer = new WaveFileWriter(wavPath, _capture.WaveFormat);
            _capture.DataAvailable += (_, e) =>
            {
                lock (_lock) { _writer?.Write(e.Buffer, 0, e.BytesRecorded); }
            };
            _capture.StartRecording();
            _segments.Add(wavPath);
        }
    }

    public void StopSegment()
    {
        lock (_lock) StopCore();
    }

    private void StopCore()
    {
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
        _silence?.Stop();
        _silence?.Dispose();
        _silence = null;
        _writer?.Dispose();
        _writer = null;
    }

    public void Dispose() => StopSegment();
}
