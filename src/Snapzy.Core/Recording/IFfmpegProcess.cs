using System.Diagnostics;

namespace Snapzy.Core.Recording;

public interface IFfmpegProcess
{
    bool HasExited { get; }
    void WriteQuit();
    /// <summary>Waits for exit up to the timeout; kills the process if it does not exit. Returns exit code (-1 if killed).</summary>
    Task<int> WaitForExitAsync(TimeSpan timeout);
}

public sealed class FfmpegProcess : IFfmpegProcess
{
    private readonly Process _process;

    public FfmpegProcess(ProcessStartInfo psi, string stderrLogFile)
    {
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardError = true;
        _process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg failed to start");
        _ = Task.Run(async () =>
        {
            try
            {
                using var writer = new StreamWriter(stderrLogFile, append: true);
                string? line;
                while ((line = await _process.StandardError.ReadLineAsync()) is not null)
                    await writer.WriteLineAsync(line);
            }
            catch (Exception) { /* logging only */ }
        });
    }

    public bool HasExited => _process.HasExited;

    public void WriteQuit()
    {
        try
        {
            if (_process.HasExited) return;
            _process.StandardInput.Write('q');
            _process.StandardInput.Flush();
        }
        catch (Exception) { /* already gone */ }
    }

    public async Task<int> WaitForExitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(cts.Token);
            return _process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { _process.Kill(entireProcessTree: true); } catch (Exception) { }
            return -1;
        }
    }
}
