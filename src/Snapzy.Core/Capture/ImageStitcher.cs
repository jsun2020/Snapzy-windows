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
    ///
    /// Two tiers: an exact per-band-hash pass first (precise, fast), and when
    /// that fails a tolerant pass using per-band brightness-bucket signatures.
    /// The tolerant tier absorbs screen-position-fixed pixel perturbations that
    /// ride on top of scrolling content - DLP screen watermarks, cursor
    /// highlighter halos, remote-desktop requantization - which shift every
    /// captured row slightly and make exact matching impossible. Both tiers
    /// compare three horizontal bands with 2-of-3 majority so a localized
    /// overlay corrupting one band cannot break the match.
    /// </summary>
    public static (int NewContentOffset, int StaticBottomRows)? FindOverlap(
        Bitmap prev, Bitmap next, int probeRows = 32)
    {
        if (prev.Width != next.Width) return null;
        probeRows = Math.Min(probeRows, Math.Min(prev.Height, next.Height));

        var exact = Find(RowBandHashes(prev), RowBandHashes(next),
            prev.Height, next.Height, probeRows, RowsMatch);
        if (exact is not null) return exact;

        // The tolerant comparator is individually laxer per row, so demand a
        // probe run twice as deep - a false alignment must then hold across
        // multiple content rows, not just one.
        var tolerantProbe = Math.Min(probeRows * 2, Math.Min(prev.Height, next.Height));
        return Find(RowBandBuckets(prev), RowBandBuckets(next),
            prev.Height, next.Height, tolerantProbe, RowsMatchTolerant);
    }

    private static (int NewContentOffset, int StaticBottomRows)? Find<T>(
        T[] prevSig, T[] nextSig, int prevHeight, int nextHeight, int probeRows,
        Func<T[], int, T[], int, bool> rowsMatch)
    {
        // Static furniture: identical suffix rows shared by both frames.
        var maxSuffix = Math.Min(prevHeight, nextHeight);
        var suffix = 0;
        while (suffix < maxSuffix &&
               rowsMatch(prevSig, prevHeight - 1 - suffix, nextSig, nextHeight - 1 - suffix))
            suffix++;

        var contentEnd = nextHeight - suffix;
        if (contentEnd < probeRows) return (contentEnd, suffix); // fully identical = no movement

        // Probe strip: the last content rows of prev, just above the furniture.
        var probeStart = prevHeight - suffix - probeRows;

        // Prefer the LAST match so repeated page elements near the top cannot win.
        for (var y = contentEnd - probeRows; y >= 0; y--)
        {
            var match = true;
            for (var i = 0; i < probeRows; i++)
            {
                if (!rowsMatch(prevSig, probeStart + i, nextSig, y + i)) { match = false; break; }
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

    // ---- Tolerant tier: per-band brightness-bucket signatures ----
    // Each band is split into 16 buckets; each bucket stores the mean luma of
    // its pixels. A band matches when at most 2 of its 16 buckets differ by
    // more than the tolerance, so a watermark stroke or halo edge crossing a
    // couple of buckets is forgiven while genuinely different content (whose
    // buckets differ broadly) is still rejected over a 32-row probe run.
    private const int BucketsPerBand = 16;
    private const int SigLen = Bands * BucketsPerBand;
    private const int BucketTolerance = 10;
    private const int MinBucketsOk = BucketsPerBand - 2;

    private static bool RowsMatchTolerant(byte[] a, int ya, byte[] b, int yb)
    {
        var okBands = 0;
        var baseA = ya * SigLen;
        var baseB = yb * SigLen;
        for (var band = 0; band < Bands; band++)
        {
            var ok = 0;
            var off = band * BucketsPerBand;
            for (var i = 0; i < BucketsPerBand; i++)
            {
                var d = a[baseA + off + i] - b[baseB + off + i];
                if (d >= -BucketTolerance && d <= BucketTolerance) ok++;
            }
            if (ok >= MinBucketsOk) okBands++;
        }
        return okBands >= Bands - 1;
    }

    private static byte[] RowBandBuckets(Bitmap bmp)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        var sig = new byte[h * SigLen];
        var bucketOfX = new int[w];
        var countPerBucket = new int[SigLen];
        for (var x = 0; x < w; x++)
        {
            var band = Math.Min(x * Bands / w, Bands - 1);
            var bandStart = band * w / Bands;
            var bandWidth = Math.Max(1, (band + 1) * w / Bands - bandStart);
            var bucket = Math.Min((x - bandStart) * BucketsPerBand / bandWidth, BucketsPerBand - 1);
            var idx = band * BucketsPerBand + bucket;
            bucketOfX[x] = idx;
            countPerBucket[idx]++;
        }
        var data = bmp.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var sums = new int[SigLen];
                for (var y = 0; y < h; y++)
                {
                    Array.Clear(sums);
                    var row = (byte*)data.Scan0 + y * data.Stride;
                    for (var x = 0; x < w; x++)
                    {
                        var p = row + x * 4;
                        sums[bucketOfX[x]] += (p[0] + 2 * p[1] + p[2]) >> 2; // luma approx (BGRA)
                    }
                    for (var i = 0; i < SigLen; i++)
                        sig[y * SigLen + i] = countPerBucket[i] > 0 ? (byte)(sums[i] / countPerBucket[i]) : (byte)0;
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return sig;
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
