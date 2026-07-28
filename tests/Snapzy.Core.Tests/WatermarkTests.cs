using System.Drawing;
using Snapzy.Core.Editing;
using Snapzy.Core.Settings;
using Xunit;

namespace Snapzy.Core.Tests;

public class WatermarkLayoutTests
{
    [Theory]
    [InlineData(WatermarkPosition.TopLeft, 16, 16)]
    [InlineData(WatermarkPosition.TopCenter, 450, 16)]
    [InlineData(WatermarkPosition.TopRight, 884, 16)]
    [InlineData(WatermarkPosition.Center, 450, 275)]
    [InlineData(WatermarkPosition.BottomLeft, 16, 534)]
    [InlineData(WatermarkPosition.BottomCenter, 450, 534)]
    [InlineData(WatermarkPosition.BottomRight, 884, 534)]
    public void Anchor_PlacesEveryPosition(WatermarkPosition pos, float wantX, float wantY)
    {
        // image 1000x600, text 100x50, margin 16
        var (x, y) = WatermarkLayout.Anchor(1000, 600, 100, 50, pos);
        Assert.Equal(wantX, x);
        Assert.Equal(wantY, y);
    }

    [Fact]
    public void Tile_CoversImageIncludingEdges()
    {
        var pts = WatermarkLayout.Tile(1000, 600, 200, 150);
        Assert.NotEmpty(pts);
        Assert.Contains(pts, p => p.X <= 0 && p.Y <= 0);        // beyond top-left
        Assert.Contains(pts, p => p.X >= 1000);                 // beyond right
        Assert.Contains(pts, p => p.Y >= 600);                  // beyond bottom
    }

    [Fact]
    public void Tile_RejectsDegenerateSteps()
    {
        Assert.Empty(WatermarkLayout.Tile(1000, 600, 0, 0));
    }

    [Theory]
    [InlineData("Tile", WatermarkPosition.Tile)]
    [InlineData("bottomright", WatermarkPosition.BottomRight)]
    [InlineData("nonsense", WatermarkPosition.Tile)]
    [InlineData(null, WatermarkPosition.Tile)]
    public void ParsePosition_IsForgiving(string? name, WatermarkPosition want)
    {
        Assert.Equal(want, WatermarkLayout.ParsePosition(name));
    }

    [Fact]
    public void AutoFontSize_Clamps()
    {
        Assert.Equal(14f, WatermarkLayout.AutoFontSize(100));
        Assert.Equal(72f, WatermarkLayout.AutoFontSize(10000));
        Assert.Equal(50f, WatermarkLayout.AutoFontSize(1000));
    }
}

public class WatermarkRendererTests
{
    private static int CountNonBlack(Bitmap bmp)
    {
        var n = 0;
        for (var y = 0; y < bmp.Height; y += 3)
            for (var x = 0; x < bmp.Width; x += 3)
            {
                var p = bmp.GetPixel(x, y);
                if (p.R > 16 || p.G > 16 || p.B > 16) n++;
            }
        return n;
    }

    [Fact]
    public void Draw_Tile_PaintsPixels()
    {
        using var bmp = new Bitmap(400, 300);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Black);
        WatermarkRenderer.Draw(bmp, "snapzy", WatermarkPosition.Tile, 0, 100, "#FFFFFF");
        Assert.True(CountNonBlack(bmp) > 50, "tiled watermark should paint many pixels");
    }

    [Fact]
    public void Draw_BottomRight_PaintsOnlyNearCorner()
    {
        using var bmp = new Bitmap(400, 300);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Black);
        WatermarkRenderer.Draw(bmp, "wm", WatermarkPosition.BottomRight, 24, 100, "#FFFFFF");
        using var topLeft = bmp.Clone(new Rectangle(0, 0, 200, 150), bmp.PixelFormat);
        using var botRight = bmp.Clone(new Rectangle(200, 150, 200, 150), bmp.PixelFormat);
        Assert.Equal(0, CountNonBlack(topLeft));
        Assert.True(CountNonBlack(botRight) > 0);
    }

    [Fact]
    public void Apply_DisabledOrEmptyText_LeavesImageUntouched()
    {
        using var bmp = new Bitmap(100, 80);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Black);
        WatermarkRenderer.Apply(bmp, new WatermarkOptions { Enabled = false, Text = "x" });
        WatermarkRenderer.Apply(bmp, new WatermarkOptions { Enabled = true, Text = "  " });
        Assert.Equal(0, CountNonBlack(bmp));
    }

    [Fact]
    public void ParseColor_FallsBackOnGarbage()
    {
        var c = WatermarkRenderer.ParseColor("not-a-color");
        Assert.Equal((0xFF, 0x3B, 0x30), (c.R, c.G, c.B));
        var ok = WatermarkRenderer.ParseColor("#00FF00");
        Assert.Equal((0, 255, 0), (ok.R, ok.G, ok.B));
    }
}
