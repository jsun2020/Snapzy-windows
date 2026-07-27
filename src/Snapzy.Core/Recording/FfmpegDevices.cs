using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Snapzy.Core.Recording;

public static class FfmpegDevices
{
    public static List<string> ParseDshowAudio(string stderr)
    {
        var result = new List<string>();
        foreach (var line in stderr.Split('\n'))
        {
            var m = Regex.Match(line, "\"(.+)\"\\s*\\(audio\\)");
            if (m.Success) result.Add(m.Groups[1].Value);
        }
        return result;
    }

    public static List<string> ListDshowAudio(string ffmpegExe)
    {
        try
        {
            var psi = new ProcessStartInfo(ffmpegExe)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
            };
            foreach (var a in new[] { "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy" })
                psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc is null) return new();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(10_000);
            return ParseDshowAudio(stderr);
        }
        catch (Exception ex)
        {
            Log.Error("dshow device enumeration failed", ex);
            return new();
        }
    }
}
