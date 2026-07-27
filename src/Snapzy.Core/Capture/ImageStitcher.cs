using System.Drawing;
using System.Drawing.Imaging;

namespace Snapzy.Core.Capture;

public static class ImageStitcher
{
    /// <summary>
    /// Finds where the bottom probeRows of prev reappear in next.
    /// Returns the index of the first NEW row in next; next.Height when the
    /// view did not move; -1 when no overlap is found.
    /// </summary>
    public static int FindNewContentOffset(Bitmap prev, Bitmap next, int probeRows = 32)
    {
        if (prev.Width != next.Width) return -1;
        probeRows = Math.Min(probeRows, Math.Min(prev.Height, next.Height));
        var prevHashes = RowHashes(prev);
        var nextHashes = RowHashes(next);
        var probe = prevHashes[^probeRows..];

        // Prefer the LAST match so repeated page furniture near the top cannot win.
        for (var y = next.Height - probeRows; y >= 0; y--)
        {
            var match = true;
            for (var i = 0; i < probeRows; i++)
            {
                if (nextHashes[y + i] != probe[i]) { match = false; break; }
            }
            if (match) return y + probeRows;
        }
        return -1;
    }

    public static Bitmap AppendNewRows(Bitmap accumulated, Bitmap next, int newContentOffset)
    {
        var newRows = next.Height - newContentOffset;
        var result = new Bitmap(accumulated.Width, accumulated.Height + newRows, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.DrawImageUnscaled(accumulated, 0, 0);
        g.DrawImage(next,
            new Rectangle(0, accumulated.Height, next.Width, newRows),
            new Rectangle(0, newContentOffset, next.Width, newRows),
            GraphicsUnit.Pixel);
        return result;
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
