using System.IO;
using Snapzy.Core;
using Snapzy.Core.History;
using Snapzy.Core.Recording;
using Snapzy.Core.Localization;
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
    private static RecordingController? _recorder;
    private static Recorder.RecordingHud? _hud;

    public static async void ToggleRecording()
    {
        try
        {
            Log.Info("Record toggle invoked, state=" + (_recorder?.State.ToString() ?? "none"));
            if (_recorder is not null && _recorder.State != RecordingState.Idle)
            {
                await StopRecordingAsync();
                return;
            }
            if (!File.Exists(AppPaths.FfmpegExe))
            {
                Tray?.Balloon("Snapzy", Strings.Get("Toast_FfmpegMissing"));
                return;
            }
            var sel = Overlay.OverlayWindow.ShowAndSelect();
            if (sel is null) return;

            var opts = new RecordingOptions
            {
                OffsetX = sel.Rect.X,
                OffsetY = sel.Rect.Y,
                Width = sel.Rect.Width,
                Height = sel.Rect.Height,
                Fps = Settings.RecordingFps,
                Cursor = Settings.RecordCursor,
                MicDevice = Settings.MicDevice,
            };
            var baseName = Path.GetFileNameWithoutExtension(FileNamer.NewCaptureName(DateTime.Now, "mp4"));
            _recorder = new RecordingController(AppPaths.FfmpegExe, AppPaths.CapturesDir);
            if (!await _recorder.StartAsync(opts, baseName))
            {
                _recorder = null;
                Tray?.Balloon("Snapzy", Strings.Get("Toast_CaptureFailed"));
                return;
            }
            Tray?.SetRecording(true);
            _hud = new Recorder.RecordingHud(sel.Rect, PauseResumeAsync, () => _ = StopRecordingWrapperAsync());
            _hud.Show();
            Log.Info($"Recording started: {sel.Rect} fps={opts.Fps}");
        }
        catch (Exception ex)
        {
            Log.Error("ToggleRecording failed", ex);
        }
    }

    private static async Task<bool> PauseResumeAsync()
    {
        if (_recorder is null) return false;
        if (_recorder.State == RecordingState.Recording)
        {
            await _recorder.PauseAsync();
            return true;
        }
        await _recorder.ResumeAsync();
        return false;
    }

    private static async Task StopRecordingWrapperAsync()
    {
        try { await StopRecordingAsync(); }
        catch (Exception ex) { Log.Error("Stop recording failed", ex); }
    }

    private static async Task StopRecordingAsync()
    {
        var rec = _recorder;
        if (rec is null) return;
        _hud?.Close();
        _hud = null;
        Tray?.SetRecording(false);
        var result = await rec.StopAsync(Settings.RecordingOutput);
        _recorder = null;
        if (result.Error is not null)
        {
            Log.Error("Recording failed: " + result.Error);
            Tray?.Balloon("Snapzy", Strings.Get("Toast_CaptureFailed") + ": " + result.Error);
            return;
        }
        HistoryEntry? entry = null;
        if (result.Mp4Path is not null) entry = History.Add(Path.GetFileName(result.Mp4Path), "video");
        if (result.GifPath is not null) entry = History.Add(Path.GetFileName(result.GifPath), "gif");
        Log.Info($"Recording saved: {result.Mp4Path ?? result.GifPath}");
        Tray?.Balloon("Snapzy", Strings.Get("Toast_RecordingSaved"));
        if (Settings.Recording.ShowQuickAccess && entry is not null)
            QuickAccess.QuickAccessWindow.ShowFor(entry, History, Settings);
    }

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
