using System.IO;
using System.Windows;
using Snapzy.Core;
using Snapzy.Core.Localization;
using Snapzy.Core.Settings;
using Snapzy.App.Tray;

namespace Snapzy.App;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, "SnapzyWindowsSingleInstance", out var isNew);
        if (!isNew) { Shutdown(); return; }

        if (!AppPaths.IsWritable())
        {
            System.Windows.MessageBox.Show(
                "Snapzy cannot write to its folder. If you are running from a zip, extract it first.",
                "Snapzy", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(); return;
        }

        var firstRun = !File.Exists(AppPaths.SettingsFile);
        AppPaths.EnsureDirs();
        Log.Init(AppPaths.LogsDir);
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("Unhandled", ex.Exception);
            ex.Handled = true;
            ShowCrashDialog(ex.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Log.Error("Fatal", ex.ExceptionObject as Exception);

        var settings = SettingsStore.Load(AppPaths.SettingsFile);
        Strings.SetLanguage(settings.Language);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        AppActions.Initialize(settings);
        _tray = new TrayIcon();
        AppActions.Tray = _tray;
        _tray.Show();

        if (firstRun)
        {
            SettingsStore.Save(settings, AppPaths.SettingsFile);
            _tray.Balloon("Snapzy", Strings.Get("FirstRun_Welcome"));
        }
        AppActions.History.CleanupOlderThan(settings.RetentionDays);
        Log.Info("Snapzy started");
    }

    private static void ShowCrashDialog(Exception ex)
    {
        var pick = System.Windows.MessageBox.Show(
            "Snapzy hit an unexpected error:\n\n" + ex.Message + "\n\nOpen the log folder?",
            "Snapzy", MessageBoxButton.YesNo, MessageBoxImage.Error);
        if (pick == MessageBoxResult.Yes)
            System.Diagnostics.Process.Start("explorer.exe", AppPaths.LogsDir);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
