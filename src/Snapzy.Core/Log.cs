namespace Snapzy.Core;

public static class Log
{
    private static readonly object Lock = new();
    private static string? _file;

    public static string? FilePath => _file;

    public static void Init(string logsDir)
    {
        if (_file is not null) return;
        Directory.CreateDirectory(logsDir);
        _file = Path.Combine(logsDir, "app.log");
    }

    public static void Info(string message) => WriteLine("INFO", message);

    public static void Error(string message, Exception? ex = null)
    {
        WriteLine("ERROR", ex is null ? message : message + Environment.NewLine + ex);
    }

    private static void WriteLine(string level, string message)
    {
        if (_file is null) return;
        lock (Lock)
        {
            try
            {
                File.AppendAllText(_file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {message}{Environment.NewLine}");
            }
            catch (IOException) { /* logging must never take the app down */ }
        }
    }
}
