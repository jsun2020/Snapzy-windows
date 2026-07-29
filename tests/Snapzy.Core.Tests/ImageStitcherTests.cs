using System.Drawing;
using System.Drawing.Imaging;
using Snapzy.Core.Capture;

public class ImageStitcherTests
{
    // Deterministic scrolled views: every content row carries a unique
    // HORIZONTAL TEXTURE (a color per 8px block, derived from a well-mixed
    // hash of row and block), like real content. Uniform single-color rows
    // would be adversarial-and-unrealistic for tolerant matching: their whole
    // signature collapses to one luma value. The mix must be non-affine or
    // rows a constant distance apart get systematically-close colors. An
    // optional constant "furniture" band at the bottom mimics scrollbars/
    // padding that never scroll.
    internal static Color DocColor(int doc, int block = 0)
    {
        var v = (uint)doc * 2654435761u ^ (uint)block * 2246822519u;
        v ^= v >> 15; v *= 2654435761u; v ^= v >> 13;
        return Color.FromArgb(255, (int)(v & 0xFF), (int)((v >> 8) & 0xFF), (int)((v >> 16) & 0xFF));
    }

    private static Bitmap View(int docStartRow, int width, int height, int furnitureRows = 0)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            var furniture = y >= height - furnitureRows;
            for (var x = 0; x < width; x++)
            {
                bmp.SetPixel(x, y, furniture
                    ? Color.FromArgb(255, 50, 50, 50)
                    : DocColor(docStartRow + y, x / 8));
            }
        }
        return bmp;
    }

    [Fact]
    public void FindOverlap_DetectsScrolledOverlap()
    {
        using var prev = View(0, 40, 200);    // doc rows 0..199
        using var next = View(120, 40, 200);  // doc rows 120..319 -> overlap = 80 rows
        var m = ImageStitcher.FindOverlap(prev, next);
        Assert.NotNull(m);
        Assert.Equal(80, m.Value.NewContentOffset); // rows 0..79 of next repeat prev's tail
        Assert.Equal(0, m.Value.StaticBottomRows);
    }

    [Fact]
    public void FindOverlap_NoScroll_ReportsNoNewContent()
    {
        using var prev = View(0, 40, 200);
        using var next = View(0, 40, 200);
        var m = ImageStitcher.FindOverlap(prev, next);
        Assert.NotNull(m);
        // Everything identical: new content starts at the content end (nothing new).
        Assert.Equal(next.Height - m.Value.StaticBottomRows, m.Value.NewContentOffset);
    }

    [Fact]
    public void FindOverlap_NoOverlap_ReturnsNull()
    {
        using var prev = View(0, 40, 200);
        using var next = View(5000, 40, 200);
        Assert.Null(ImageStitcher.FindOverlap(prev, next));
    }

    [Fact]
    public void FindOverlap_WithStaticBottomFurniture_IgnoresIt()
    {
        // Regression: a scrollbar band at the bottom must not fake a "no move" match.
        using var prev = View(0, 40, 200, furnitureRows: 36);
        using var next = View(120, 40, 200, furnitureRows: 36);
        var m = ImageStitcher.FindOverlap(prev, next);
        Assert.NotNull(m);
        Assert.Equal(36, m.Value.StaticBottomRows);
        // prev content = docs 0..163; next content = docs 120..283; overlap 44 rows.
        Assert.Equal(44, m.Value.NewContentOffset);
    }

    [Fact]
    public void FindOverlap_ToleratesLocalizedOverlayInOneBand()
    {
        // A software cursor halo (locate-pointer overlay) sits in the right
        // third of the next frame, right where prev's probe strip lands after
        // the scroll. 2-of-3 band majority must still find the overlap.
        using var prev = View(0, 90, 200);
        using var next = View(120, 90, 200);
        for (var y = 60; y < 100; y++)             // overlap region rows
            for (var x = 65; x < 85; x++)          // inside the right band only
                next.SetPixel(x, y, Color.Magenta);
        var m = ImageStitcher.FindOverlap(prev, next);
        Assert.NotNull(m);
        Assert.Equal(80, m.Value.NewContentOffset);
    }

    [Fact]
    public void FindOverlap_AnimatedOverlayOnStaticPage_ReportsNoMovement()
    {
        // Page did not scroll; only an overlay changed in one band. Must be
        // treated as "no movement", not "stitch lost".
        using var prev = View(0, 90, 200);
        using var next = View(0, 90, 200);
        for (var y = 90; y < 120; y++)
            for (var x = 65; x < 85; x++)
                next.SetPixel(x, y, Color.Magenta);
        var m = ImageStitcher.FindOverlap(prev, next);
        Assert.NotNull(m);
        Assert.Equal(next.Height - m.Value.StaticBottomRows, m.Value.NewContentOffset);
    }

    // 15% blend toward gray along fixed diagonal stroke positions - a stand-in
    // for a corporate DLP screen watermark, which is fixed to SCREEN position
    // while the content scrolls beneath it.
    private static void ApplyScreenWatermark(Bitmap bmp)
    {
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
            {
                if ((x + y) % 23 >= 3) continue;
                var p = bmp.GetPixel(x, y);
                bmp.SetPixel(x, y, Color.FromArgb(255,
                    p.R + (128 - p.R) * 15 / 100,
                    p.G + (128 - p.G) * 15 / 100,
                    p.B + (128 - p.B) * 15 / 100));
            }
    }

    [Fact]
    public void FindOverlap_ScreenWatermarkOverScrolledContent_TolerantTierMatches()
    {
        // Exact hashing cannot match any row (every row's pixels are perturbed
        // differently at its old and new screen position); the tolerant
        // bucket tier must still find the true overlap.
        using var prev = View(0, 480, 200);
        using var next = View(120, 480, 200);
        ApplyScreenWatermark(prev);
        ApplyScreenWatermark(next);
        var m = ImageStitcher.FindOverlap(prev, next);
        Assert.NotNull(m);
        Assert.Equal(80, m.Value.NewContentOffset);
    }

    [Fact]
    public void FindOverlap_GlobalBrightnessShift_TolerantTierMatches()
    {
        // Uniform +6 luma on the next frame only (remote-desktop requantization
        // class): exact tier fails everywhere, tolerant tier must match.
        using var prev = View(0, 90, 200);
        using var next = View(120, 90, 200);
        for (var y = 0; y < next.Height; y++)
            for (var x = 0; x < next.Width; x++)
            {
                var p = next.GetPixel(x, y);
                next.SetPixel(x, y, Color.FromArgb(255,
                    Math.Min(255, p.R + 6), Math.Min(255, p.G + 6), Math.Min(255, p.B + 6)));
            }
        var m = ImageStitcher.FindOverlap(prev, next);
        Assert.NotNull(m);
        Assert.Equal(80, m.Value.NewContentOffset);
    }

    [Fact]
    public void FindOverlap_WatermarkedButDifferentContent_StillNull()
    {
        // The tolerant tier must not hallucinate overlap between genuinely
        // different content just because both carry the same watermark.
        using var prev = View(0, 480, 200);
        using var next = View(5000, 480, 200);
        ApplyScreenWatermark(prev);
        ApplyScreenWatermark(next);
        Assert.Null(ImageStitcher.FindOverlap(prev, next));
    }

    [Fact]
    public void AppendNewRows_GrowsByNewContentAndTrimsFurniture()
    {
        using var prev = View(0, 40, 200, furnitureRows: 36);
        using var next = View(120, 40, 200, furnitureRows: 36);
        using var prevContent = ImageStitcher.CropBottom(prev, 36);
        using var stitched = ImageStitcher.AppendNewRows(prevContent, next, 44, 36);
        Assert.Equal(164 + (164 - 44), stitched.Height); // 164 + 120 = 284 content rows
        Assert.Equal(40, stitched.Width);
        // Bottom row of the stitch == last content row of next (doc 283)
        Assert.Equal(next.GetPixel(0, 163), stitched.GetPixel(0, 283));
    }
}
