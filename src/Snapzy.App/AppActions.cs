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

    public static void CaptureOcr()
    {
        try
        {
            if (!Snapzy.Core.Ocr.OcrService.IsAvailable)
            {
                Tray?.Balloon("Snapzy", Strings.Get("Toast_OcrUnavailable"));
                return;
            }
            var sel = Overlay.OverlayWindow.ShowAndSelect();
            if (sel is null) return;
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Thread.Sleep(120);
            var bmp = sel.Hwnd != IntPtr.Zero
                ? Snapzy.Core.Capture.ScreenCapture.CaptureWindow(sel.Hwnd)
                : Snapzy.Core.Capture.ScreenCapture.CaptureRect(sel.Rect);
            OcrAndCopy(bmp);
        }
        catch (Exception ex)
        {
            Log.Error("CaptureOcr failed", ex);
            Tray?.Balloon("Snapzy", Strings.Get("Toast_CaptureFailed"));
        }
    }

    /// <summary>Recognizes text in the bitmap and copies it. Takes ownership of the bitmap.</summary>
    internal static async void OcrAndCopy(System.Drawing.Bitmap bmp)
    {
        try
        {
            using var owned = bmp;
            if (!Snapzy.Core.Ocr.OcrService.IsAvailable)
            {
                Tray?.Balloon("Snapzy", Strings.Get("Toast_OcrUnavailable"));
                return;
            }
            var ocr = await Snapzy.Core.Ocr.OcrService.RecognizeForClipboardAsync(owned);
            if (string.IsNullOrWhiteSpace(ocr.Text))
            {
                Tray?.Balloon("Snapzy", Strings.Get("Toast_OcrEmpty"));
                return;
            }
            System.Windows.Clipboard.SetText(ocr.Text);
            Log.Info($"OCR copied {ocr.Text.Length} chars" + (ocr.IsTable ? $" as {ocr.Rows}x{ocr.Columns} table" : ""));
            Tray?.Balloon("Snapzy", ocr.IsTable
                ? string.Format(Strings.Get("Toast_OcrTableCopied"), ocr.Rows, ocr.Columns)
                : Strings.Get("Toast_OcrCopied"));
        }
        catch (Exception ex)
        {
            Log.Error("OcrAndCopy failed", ex);
            Tray?.Balloon("Snapzy", Strings.Get("Toast_CaptureFailed"));
        }
    }

    public static async void CaptureScrolling()
    {
        try
        {
            var sel = Overlay.OverlayWindow.ShowAndSelect(startInWindowMode: true);
            if (sel is null) return;
            if (sel.Hwnd == IntPtr.Zero)
            {
                Tray?.Balloon("Snapzy", Strings.Get("Toast_ScrollNeedWindow"));
                return;
            }
            var progress = new Overlay.ScrollProgressWindow();
            progress.Show();
            var result = await Task.Run(() => Snapzy.Core.Capture.ScrollCapture.Run(
                sel.Hwnd,
                onStep: s => progress.SetStep(s),
                isCancelled: () => progress.Cancelled));
            progress.Close();

            if (result.Image is null)
            {
                Log.Error("Scroll capture failed: " + result.Error);
                Tray?.Balloon("Snapzy", Strings.Get("Toast_CaptureFailed") + ": " + result.Error);
                return;
            }
            using var bmp = result.Image;
            var name = Snapzy.Core.History.FileNamer.NewCaptureName(DateTime.Now, Settings.ImageFormat);
            var path = System.IO.Path.Combine(AppPaths.CapturesDir, name);
            Snapzy.Core.Capture.ImageSaver.Save(bmp, path, Settings.ImageFormat, AppPaths.FfmpegExe);
            var entry = History.Add(name, "image");
            Log.Info($"Scroll capture saved {name} ({bmp.Width}x{bmp.Height}, {result.Steps} steps)");
            if (Settings.Screenshot.CopyToClipboard)
            {
                CaptureFlow.CopyImageToClipboard(path);
                Tray?.Balloon("Snapzy", Strings.Get("Toast_CopiedToClipboard"));
            }
            if (Settings.Screenshot.ShowQuickAccess)
                QuickAccess.QuickAccessWindow.ShowFor(entry, History, Settings);
        }
        catch (Exception ex)
        {
            Log.Error("CaptureScrolling failed", ex);
            Tray?.Balloon("Snapzy", Strings.Get("Toast_CaptureFailed"));
        }
    }
    private static RecordingController? _recorder;
    private static Recorder.RecordingHud? _hud;
    private static WasapiLoopbackRecorder? _sysAudio;

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
            var sel = Overlay.OverlayWindow.ShowAndSelect();
            if (sel is null) return;
            StartRecordingFromOverlay(sel);
        }
        catch (Exception ex)
        {
            Log.Error("ToggleRecording failed", ex);
        }
    }

    /// <summary>Starts a recording for an already-made overlay selection.</summary>
    internal static async void StartRecordingFromOverlay(Overlay.SelectionResult sel)
    {
        try
        {
            if (_recorder is not null && _recorder.State != RecordingState.Idle)
            {
                Tray?.Balloon("Snapzy", Strings.Get("Toast_AlreadyRecording"));
                return;
            }
            if (!File.Exists(AppPaths.FfmpegExe))
            {
                Tray?.Balloon("Snapzy", Strings.Get("Toast_FfmpegMissing"));
                return;
            }

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
            _sysAudio = Settings.RecordSystemAudio ? new WasapiLoopbackRecorder() : null;
            _recorder = new RecordingController(AppPaths.FfmpegExe, AppPaths.CapturesDir, null, _sysAudio);
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
            Log.Error("StartRecordingFromOverlay failed", ex);
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
        _sysAudio?.Dispose();
        _sysAudio = null;
        if (result.Error is not null)
        {
            Log.Error("Recording failed: " + result.Error);
            Tray?.Balloon("Snapzy", Strings.Get("Toast_CaptureFailed") + ": " + result.Error);
            return;
        }
        HistoryEntry? entry = null;
        if (result.Mp4Path is not null) entry = History.Add(Path.GetFileName(result.Mp4Path), "video");
        if (result.GifPath is not null) entry = History.Add(Path.GetFileName(result.GifPath), "gif");
        if (result.WebpPath is not null) entry = History.Add(Path.GetFileName(result.WebpPath), "gif");
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
    public static void OpenHistory() => HistoryBrowser.HistoryWindow.Open(History, Settings);
    public static void OpenSettings() => SettingsUI.SettingsWindow.Open(Settings, () =>
    {
        var failed = Hotkeys?.Reregister(Settings.Hotkeys) ?? new List<string>();
        if (failed.Count > 0)
            Tray?.Balloon("Snapzy", Strings.Get("Toast_HotkeyConflict") + ": " + string.Join(", ", failed));
        Tray?.RebuildMenu();
    });

    public static void Quit()
    {
        Log.Info("Quit requested");
        System.Windows.Application.Current.Shutdown();
    }
}
