using System.Drawing;
using System.Drawing.Imaging;
using Snapzy.Core.Capture;

public class ImageStitcherTests
{
    // A deterministic test image: every row y is filled with a color derived from its
    // "document row" index, so scrolled views are easy to fabricate.
    private static Bitmap View(int docStartRow, int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            // Encode the document row uniquely in the color (no periodic collisions).
            var doc = docStartRow + y;
            var c = Color.FromArgb(255, doc % 256, (doc / 256) % 256, (doc / 65536) % 256);
            for (var x = 0; x < width; x++) bmp.SetPixel(x, y, c);
        }
        return bmp;
    }

    [Fact]
    public void FindNewContentOffset_DetectsScrolledOverlap()
    {
        using var prev = View(0, 40, 200);    // doc rows 0..199
        using var next = View(120, 40, 200);  // doc rows 120..319 -> overlap = 80 rows
        var offset = ImageStitcher.FindNewContentOffset(prev, next);
        Assert.Equal(80, offset);             // rows 0..79 of next repeat prev's tail
    }

    [Fact]
    public void FindNewContentOffset_NoScroll_ReturnsHeight()
    {
        using var prev = View(0, 40, 200);
        using var next = View(0, 40, 200);
        Assert.Equal(200, ImageStitcher.FindNewContentOffset(prev, next));
    }

    [Fact]
    public void FindNewContentOffset_NoOverlap_ReturnsMinusOne()
    {
        using var prev = View(0, 40, 200);
        using var next = View(5000, 40, 200);
        Assert.Equal(-1, ImageStitcher.FindNewContentOffset(prev, next));
    }

    [Fact]
    public void AppendNewRows_GrowsByNewContent()
    {
        using var prev = View(0, 40, 200);
        using var next = View(120, 40, 200);
        using var stitched = ImageStitcher.AppendNewRows(prev, next, 80);
        Assert.Equal(320, stitched.Height);   // 200 + (200 - 80)
        Assert.Equal(40, stitched.Width);
        // Bottom row of the stitch == bottom row of next (doc row 319)
        Assert.Equal(next.GetPixel(0, 199), stitched.GetPixel(0, 319));
    }
}
