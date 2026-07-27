namespace Snapzy.Core;

public static class AppPaths
{
    public static string BaseDir { get; } = AppContext.BaseDirectory;
    public static string SettingsFile => Path.Combine(BaseDir, "portable.json");
    public static string CapturesDir => Path.Combine(BaseDir, "Captures");
    public static string LogsDir => Path.Combine(BaseDir, "logs");
    public static string HistoryFile => Path.Combine(CapturesDir, "history.json");
    public static string FfmpegExe => Path.Combine(BaseDir, "ffmpeg", "ffmpeg.exe");

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(CapturesDir);
        Directory.CreateDirectory(LogsDir);
    }

    public static bool IsWritable()
    {
        try
        {
            var probe = Path.Combine(BaseDir, ".write-probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception) { return false; }
    }
}
