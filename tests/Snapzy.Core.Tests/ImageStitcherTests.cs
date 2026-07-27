using System.Drawing;
using System.Drawing.Imaging;
using Snapzy.Core.Capture;

public class ImageStitcherTests
{
    // Deterministic scrolled views: every content row encodes its document row
    // uniquely in the pixel color; an optional constant "furniture" band at the
    // bottom mimics scrollbars/padding that never scroll.
    private static Bitmap View(int docStartRow, int width, int height, int furnitureRows = 0)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            Color c;
            if (y >= height - furnitureRows)
            {
                c = Color.FromArgb(255, 50, 50, 50); // static furniture
            }
            else
            {
                var doc = docStartRow + y;
                c = Color.FromArgb(255, doc % 256, (doc / 256) % 256, (doc / 65536) % 256);
            }
            for (var x = 0; x < width; x++) bmp.SetPixel(x, y, c);
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
