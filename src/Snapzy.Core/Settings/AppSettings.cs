namespace Snapzy.Core.Settings;

public class HotkeyBinding
{
    public string Gesture { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public class PostCaptureOptions
{
    public bool CopyToClipboard { get; set; } = true;
    public bool ShowQuickAccess { get; set; } = true;
    public bool OpenAnnotate { get; set; } = false;
}

public class AppSettings
{
    public string Language { get; set; } = "en";          // "en" | "zh-CN"
    public string Theme { get; set; } = "system";          // "light" | "dark" | "system"
    public string ImageFormat { get; set; } = "png";       // "png" | "jpg" | "webp"
    public string FullscreenMode { get; set; } = "currentMonitor"; // | "allMonitors"
    public PostCaptureOptions Screenshot { get; set; } = new();
    public PostCaptureOptions Recording { get; set; } = new() { CopyToClipboard = false };
    public int RecordingFps { get; set; } = 30;
    public string RecordingOutput { get; set; } = "mp4";   // "mp4" | "gif" | "both"
    public bool RecordCursor { get; set; } = true;
    public string MicDevice { get; set; } = "";            // empty = no audio
    public int QuickAccessTimeoutSeconds { get; set; } = 8;
    public int RetentionDays { get; set; } = 0;            // 0 = forever
    public bool LaunchAtLogin { get; set; } = false;
    public bool TrayLeftClickAreaCapture { get; set; } = true;
    public Dictionary<string, HotkeyBinding> Hotkeys { get; set; } = new();

    public static AppSettings CreateDefault() => new()
    {
        Hotkeys = new()
        {
            ["CaptureFullscreen"]   = new() { Gesture = "Ctrl+Shift+3" },
            ["CaptureArea"]         = new() { Gesture = "Ctrl+Shift+4" },
            ["CaptureAreaAnnotate"] = new() { Gesture = "Ctrl+Shift+7" },
            ["RecordToggle"]        = new() { Gesture = "Ctrl+Shift+5" },
            ["OpenAnnotate"]        = new() { Gesture = "Ctrl+Shift+A" },
            ["OpenHistory"]         = new() { Gesture = "Ctrl+Shift+H" },
        }
    };
}
