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
            var sel = OverlayWindow.ShowAndSelect();
            if (sel is null) return null;

            // Let the overlay leave the screen before capturing, or its dim
            // layer would appear in the shot.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Thread.Sleep(120);

            using var bmp = sel.Hwnd != IntPtr.Zero
                ? ScreenCapture.CaptureWindow(sel.Hwnd)
                : ScreenCapture.CaptureRect(sel.Rect);
            var entry = SaveAndIndex(bmp, settings, history);
            RunPostActions(entry, history, settings, forceAnnotate);
            return entry;
        }
        catch (Exception ex)
        {
            Fail(ex);
            return null;
        }
    }

    private static HistoryEntry SaveAndIndex(System.Drawing.Bitmap bmp, AppSettings settings, HistoryStore history)
    {
        var name = FileNamer.NewCaptureName(DateTime.Now, settings.ImageFormat);
        var path = Path.Combine(AppPaths.CapturesDir, name);
        ImageSaver.Save(bmp, path, settings.ImageFormat, AppPaths.FfmpegExe);
        var entry = history.Add(name, "image");
        Log.Info($"Captured {name} ({bmp.Width}x{bmp.Height})");
        return entry;
    }

    private static void RunPostActions(HistoryEntry entry, HistoryStore history, AppSettings settings, bool forceAnnotate)
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
