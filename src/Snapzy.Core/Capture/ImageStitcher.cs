using System.Drawing;
using System.Drawing.Imaging;

namespace Snapzy.Core.Capture;

public static class ImageStitcher
{
    /// <summary>
    /// Compares two same-width frames of a scrolling view.
    /// StaticBottomRows = identical bottom rows in both frames (scrollbars,
    /// padding - window furniture that never scrolls). NewContentOffset = index
    /// in next where unseen content begins; when it equals
    /// next.Height - StaticBottomRows the view did not move.
    /// Returns null when no overlap can be found (stitch lost).
    /// </summary>
    public static (int NewContentOffset, int StaticBottomRows)? FindOverlap(
        Bitmap prev, Bitmap next, int probeRows = 32)
    {
        if (prev.Width != next.Width) return null;
        probeRows = Math.Min(probeRows, Math.Min(prev.Height, next.Height));
        var prevHashes = RowHashes(prev);
        var nextHashes = RowHashes(next);

        // Static furniture: identical suffix rows shared by both frames.
        var maxSuffix = Math.Min(prev.Height, next.Height);
        var suffix = 0;
        while (suffix < maxSuffix &&
               prevHashes[prev.Height - 1 - suffix] == nextHashes[next.Height - 1 - suffix])
            suffix++;

        var contentEnd = next.Height - suffix;
        if (contentEnd < probeRows) return (contentEnd, suffix); // fully identical = no movement

        // Probe strip: the last content rows of prev, just above the furniture.
        var probeEnd = prev.Height - suffix;
        var probe = prevHashes[(probeEnd - probeRows)..probeEnd];

        // Prefer the LAST match so repeated page elements near the top cannot win.
        for (var y = contentEnd - probeRows; y >= 0; y--)
        {
            var match = true;
            for (var i = 0; i < probeRows; i++)
            {
                if (nextHashes[y + i] != probe[i]) { match = false; break; }
            }
            if (match) return (y + probeRows, suffix);
        }
        return null;
    }

    /// <summary>Appends next's rows [newContentOffset, next.Height - trimBottomRows) below accumulated.</summary>
    public static Bitmap AppendNewRows(Bitmap accumulated, Bitmap next, int newContentOffset, int trimBottomRows)
    {
        var newRows = next.Height - trimBottomRows - newContentOffset;
        var result = new Bitmap(accumulated.Width, accumulated.Height + Math.Max(0, newRows), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.DrawImageUnscaled(accumulated, 0, 0);
        if (newRows > 0)
        {
            g.DrawImage(next,
                new Rectangle(0, accumulated.Height, next.Width, newRows),
                new Rectangle(0, newContentOffset, next.Width, newRows),
                GraphicsUnit.Pixel);
        }
        return result;
    }

    public static Bitmap CropBottom(Bitmap source, int rows)
    {
        if (rows <= 0) return (Bitmap)source.Clone();
        return source.Clone(new Rectangle(0, 0, source.Width, source.Height - rows), PixelFormat.Format32bppArgb);
    }

    private static ulong[] RowHashes(Bitmap bmp)
    {
        var hashes = new ulong[bmp.Height];
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (var y = 0; y < bmp.Height; y++)
                {
                    var row = (byte*)data.Scan0 + y * data.Stride;
                    ulong h = 1469598103934665603UL; // FNV-1a
                    for (var x = 0; x < bmp.Width * 4; x++)
                    {
                        h ^= row[x];
                        h *= 1099511628211UL;
                    }
                    hashes[y] = h;
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return hashes;
    }
}
