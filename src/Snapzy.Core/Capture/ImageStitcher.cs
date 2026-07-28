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
    /// Rows are compared in three horizontal bands with 2-of-3 majority, so a
    /// localized overlay (software cursor halo, floating widget) that corrupts
    /// one band cannot break the match.
    /// </summary>
    public static (int NewContentOffset, int StaticBottomRows)? FindOverlap(
        Bitmap prev, Bitmap next, int probeRows = 32)
    {
        if (prev.Width != next.Width) return null;
        probeRows = Math.Min(probeRows, Math.Min(prev.Height, next.Height));
        var prevHashes = RowBandHashes(prev);
        var nextHashes = RowBandHashes(next);

        // Static furniture: identical suffix rows shared by both frames.
        var maxSuffix = Math.Min(prev.Height, next.Height);
        var suffix = 0;
        while (suffix < maxSuffix &&
               RowsMatch(prevHashes, prev.Height - 1 - suffix, nextHashes, next.Height - 1 - suffix))
            suffix++;

        var contentEnd = next.Height - suffix;
        if (contentEnd < probeRows) return (contentEnd, suffix); // fully identical = no movement

        // Probe strip: the last content rows of prev, just above the furniture.
        var probeStart = prev.Height - suffix - probeRows;

        // Prefer the LAST match so repeated page elements near the top cannot win.
        for (var y = contentEnd - probeRows; y >= 0; y--)
        {
            var match = true;
            for (var i = 0; i < probeRows; i++)
            {
                if (!RowsMatch(prevHashes, probeStart + i, nextHashes, y + i)) { match = false; break; }
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

    private const int Bands = 3;

    private static bool RowsMatch(ulong[] a, int ya, ulong[] b, int yb)
    {
        var same = 0;
        for (var i = 0; i < Bands; i++)
            if (a[ya * Bands + i] == b[yb * Bands + i]) same++;
        return same >= Bands - 1;
    }

    // Per-row FNV-1a hashes over three horizontal bands: [y*3 + band].
    private static ulong[] RowBandHashes(Bitmap bmp)
    {
        var hashes = new ulong[bmp.Height * Bands];
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = bmp.Width * 4;
            var b1 = rowBytes / Bands;
            var b2 = rowBytes * 2 / Bands;
            unsafe
            {
                for (var y = 0; y < bmp.Height; y++)
                {
                    var row = (byte*)data.Scan0 + y * data.Stride;
                    ulong h0 = 1469598103934665603UL, h1 = h0, h2 = h0;
                    for (var x = 0; x < b1; x++) { h0 ^= row[x]; h0 *= 1099511628211UL; }
                    for (var x = b1; x < b2; x++) { h1 ^= row[x]; h1 *= 1099511628211UL; }
                    for (var x = b2; x < rowBytes; x++) { h2 ^= row[x]; h2 *= 1099511628211UL; }
                    hashes[y * Bands] = h0;
                    hashes[y * Bands + 1] = h1;
                    hashes[y * Bands + 2] = h2;
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return hashes;
    }
}
