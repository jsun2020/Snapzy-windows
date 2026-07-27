namespace Snapzy.Core.Recording;

public interface ISystemAudioRecorder : IDisposable
{
    void StartSegment(string wavPath);
    void StopSegment();
    IReadOnlyList<string> Segments { get; }
}
