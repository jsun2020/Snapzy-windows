using System.IO;
using System.Windows.Media.Imaging;
using Snapzy.Core;
using Snapzy.Core.Capture;
using Snapzy.Core.History;
using Snapzy.Core.Localization;
using Snapzy.Core.Settings;
using Snapzy.App.Overlay;

namespace Snapzy.App;

public static class CaptureFlow
{
    public static HistoryEntry? RunFullscreen(AppSettings settings, HistoryStore history)
    {
        try
        {
            var rect = settings.FullscreenMode == "allMonitors"
                ? System.Windows.Forms.SystemInformation.VirtualScreen
                : System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).Bounds;
            using var bmp = ScreenCapture.CaptureRect(rect);
            var entry = SaveAndIndex(bmp, settings, history);
            RunPostActions(entry, history, settings, forceAnnotate: false);
            return entry;
        }
        catch (Exception ex)
        {
            Fail(ex);
            return null;
        }
    }

    public static HistoryEntry? RunArea(AppSettings settings, HistoryStore history, bool forceAnnotate)
    {
        try
        {
            // The floating toolbar only appears in the general capture flow;
            // the annotate hotkey already knows its destination.
            var sel = OverlayWindow.ShowAndSelect(showToolbar: !forceAnnotate);
            if (sel is null) return null;

            if (sel.Action == OverlayAction.Record)
            {
                AppActions.StartRecordingFromOverlay(sel);
                return null;
            }
            if (sel.Action == OverlayAction.Scroll)
            {
                AppActions.ScrollCaptureWindow(sel.Hwnd);
                return null;
            }

            // Let the overlay leave the screen before capturing, or its dim
            // layer would appear in the shot.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Thread.Sleep(120);

            var bmp = sel.Hwnd != IntPtr.Zero
                ? ScreenCapture.CaptureWindow(sel.Hwnd)
                : ScreenCapture.CaptureRect(sel.Rect);
            if (sel.Action == OverlayAction.Ocr)
            {
                AppActions.OcrAndCopy(bmp); // takes ownership; nothing is saved
                return null;
            }
            using (bmp)
            {
                var entry = SaveAndIndex(bmp, settings, history);
                RunPostActions(entry, history, settings,
                    forceAnnotate || sel.Action == OverlayAction.Annotate);
                return entry;
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
            return null;
        }
    }

    internal static HistoryEntry SaveAndIndex(System.Drawing.Bitmap bmp, AppSettings settings, HistoryStore history)
    {
        // Single chokepoint: every screenshot (area, fullscreen, scrolling)
        // gets the watermark here while it is enabled.
        Snapzy.Core.Editing.WatermarkRenderer.Apply(bmp, settings.Watermark);
        var name = FileNamer.NewCaptureName(DateTime.Now, settings.ImageFormat);
        var path = Path.Combine(AppPaths.CapturesDir, name);
        ImageSaver.Save(bmp, path, settings.ImageFormat, AppPaths.FfmpegExe);
        var entry = history.Add(name, "image");
        Log.Info($"Captured {name} ({bmp.Width}x{bmp.Height})");
        return entry;
    }

    internal static void RunPostActions(HistoryEntry entry, HistoryStore history, AppSettings settings, bool forceAnnotate)
    {
        var path = history.GetFullPath(entry);
        var opts = settings.Screenshot;

        if (opts.CopyToClipboard)
        {
            CopyImageToClipboard(path);
            AppActions.Tray?.Balloon("Snapzy", Strings.Get("Toast_CopiedToClipboard"));
        }
        if (forceAnnotate || opts.OpenAnnotate)
        {
            AppActions.OpenAnnotate(path);
        }
        else if (opts.ShowQuickAccess)
        {
            ShowQuickAccess(entry, history, settings);
        }
    }

    private static void ShowQuickAccess(HistoryEntry entry, HistoryStore history, AppSettings settings)
    {
        QuickAccess.QuickAccessWindow.ShowFor(entry, history, settings);
    }

    public static void CopyImageToClipboard(string path)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.UriSource = new Uri(path);
        img.EndInit();
        img.Freeze();
        System.Windows.Clipboard.SetImage(img);
    }

    private static void Fail(Exception ex)
    {
        Log.Error("Capture failed", ex);
        AppActions.Tray?.Balloon("Snapzy", Strings.Get("Toast_CaptureFailed") + ": " + ex.Message);
    }
}
