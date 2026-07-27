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

    public static void CaptureFullscreen() => CaptureFlow.RunFullscreen(Settings, History);
    public static void CaptureArea() => CaptureFlow.RunArea(Settings, History, forceAnnotate: false);
    public static void CaptureAreaAnnotate() => CaptureFlow.RunArea(Settings, History, forceAnnotate: true);
    public static void ToggleRecording() => Stub(nameof(ToggleRecording));

    public static void OpenAnnotate(string? imagePath)
    {
        if (imagePath is null)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg",
                InitialDirectory = AppPaths.CapturesDir,
            };
            if (dlg.ShowDialog() != true) return;
            imagePath = dlg.FileName;
        }
        Annotate.AnnotateWindow.Open(imagePath, null, History);
    }
    public static void OpenHistory() => Stub(nameof(OpenHistory));
    public static void OpenSettings() => Stub(nameof(OpenSettings));

    public static void Quit()
    {
        Log.Info("Quit requested");
        System.Windows.Application.Current.Shutdown();
    }
}
