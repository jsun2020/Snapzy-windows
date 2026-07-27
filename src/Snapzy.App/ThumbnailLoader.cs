using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Snapzy.Core;
using Snapzy.Core.History;

namespace Snapzy.App;

public static class ThumbnailLoader
{
    public static BitmapSource? GetThumb(HistoryEntry entry, HistoryStore store, int decodeWidth = 260)
    {
        try
        {
            var path = store.GetFullPath(entry);
            if (!File.Exists(path)) return null;
            if (entry.Type == "image") return LoadBitmap(path, decodeWidth);

            var thumbsDir = Path.Combine(AppPaths.CapturesDir, ".thumbs");
            Directory.CreateDirectory(thumbsDir);
            var thumbPath = Path.Combine(thumbsDir, entry.Id + ".png");
            if (!File.Exists(thumbPath) && File.Exists(AppPaths.FfmpegExe))
            {
                var psi = new ProcessStartInfo(AppPaths.FfmpegExe)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                };
                foreach (var a in new[] { "-y", "-hide_banner", "-i", path, "-frames:v", "1", thumbPath })
                    psi.ArgumentList.Add(a);
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10_000);
            }
            return File.Exists(thumbPath) ? LoadBitmap(thumbPath, decodeWidth) : null;
        }
        catch (Exception ex)
        {
            Log.Error("Thumbnail load failed", ex);
            return null;
        }
    }

    private static BitmapSource LoadBitmap(string path, int decodeWidth)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.DecodePixelWidth = decodeWidth;
        img.UriSource = new Uri(path);
        img.EndInit();
        img.Freeze();
        return img;
    }
}
