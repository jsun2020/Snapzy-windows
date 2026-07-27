using Snapzy.Core;
using Snapzy.Core.History;
using Snapzy.Core.Settings;
using Snapzy.App.Tray;

namespace Snapzy.App;

public static class AppActions
{
    public static AppSettings Settings { get; private set; } = AppSettings.CreateDefault();
    public static HistoryStore History { get; private set; } = null!;
    public static TrayIcon? Tray { get; set; }
    public static Hotkeys.HotkeyManager? Hotkeys { get; set; }

    public static void Initialize(AppSettings settings)
    {
        Settings = settings;
        History = new HistoryStore(AppPaths.CapturesDir);
    }

    private static void Stub(string name)
    {
        Log.Info($"Action invoked (stub): {name}");
        Tray?.Balloon("Snapzy", $"Coming soon: {name}");
    }

    public static void CaptureFullscreen() => Stub(nameof(CaptureFullscreen));

    public static void CaptureArea()
    {
        var r = Overlay.OverlayWindow.ShowAndSelect();
        Log.Info(r is null
            ? "Overlay cancelled"
            : $"Overlay result: {r.Rect.X},{r.Rect.Y} {r.Rect.Width}x{r.Rect.Height} hwnd={r.Hwnd}");
    }
    public static void CaptureAreaAnnotate() => Stub(nameof(CaptureAreaAnnotate));
    public static void ToggleRecording() => Stub(nameof(ToggleRecording));
    public static void OpenAnnotate(string? imagePath) => Stub(nameof(OpenAnnotate));
    public static void OpenHistory() => Stub(nameof(OpenHistory));
    public static void OpenSettings() => Stub(nameof(OpenSettings));

    public static void Quit()
    {
        Log.Info("Quit requested");
        System.Windows.Application.Current.Shutdown();
    }
}
