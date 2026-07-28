using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Snapzy.Core.Settings;

namespace Snapzy.Core.Editing;

public enum WatermarkPosition
{
    Tile, TopLeft, TopCenter, TopRight, Center, BottomLeft, BottomCenter, BottomRight,
}

/// <summary>Pure geometry for watermark placement (shared by renderer and overlay preview).</summary>
public static class WatermarkLayout
{
    public static WatermarkPosition ParsePosition(string? name) =>
        Enum.TryParse<WatermarkPosition>(name, ignoreCase: true, out var p) ? p : WatermarkPosition.Tile;

    /// <summary>Font size in pixels when the user picked "auto" (0).</summary>
    public static float AutoFontSize(int imgW) => Math.Clamp(imgW / 20f, 14f, 72f);

    public static (float X, float Y) Anchor(
        int imgW, int imgH, float textW, float textH, WatermarkPosition pos, float margin = 16f)
    {
        var x = pos switch
        {
            WatermarkPosition.TopLeft or WatermarkPosition.BottomLeft => margin,
            WatermarkPosition.TopCenter or WatermarkPosition.Center or WatermarkPosition.BottomCenter
                => (imgW - textW) / 2f,
            _ => imgW - textW - margin,
        };
        var y = pos switch
        {
            WatermarkPosition.TopLeft or WatermarkPosition.TopCenter or WatermarkPosition.TopRight => margin,
            WatermarkPosition.Center => (imgH - textH) / 2f,
            _ => imgH - textH - margin,
        };
        return (x, y);
    }

    /// <summary>
    /// Staggered tile origins covering the image, extended one step past every
    /// edge so rotated text has no bare corners.
    /// </summary>
    public static List<(float X, float Y)> Tile(int imgW, int imgH, float stepX, float stepY)
    {
        var pts = new List<(float X, float Y)>();
        if (stepX < 8f || stepY < 8f) return pts;
        var row = 0;
        for (var y = -stepY; y < imgH + stepY; y += stepY, row++)
        {
            var offset = row % 2 == 0 ? 0f : stepX / 2f;
            for (var x = -stepX + offset; x < imgW + stepX; x += stepX)
                pts.Add((x, y));
        }
        return pts;
    }
}

/// <summary>Draws a text watermark onto a captured bitmap.</summary>
public static class WatermarkRenderer
{
    public const float TileAngle = -30f;
    public const float TileStepXFactor = 1.6f;  // of text width
    public const float TileStepYFactor = 4f;    // of text height

    public static void Apply(Bitmap bmp, WatermarkOptions opts)
    {
        if (!opts.Enabled || string.IsNullOrWhiteSpace(opts.Text)) return;
        Draw(bmp, opts.Text, WatermarkLayout.ParsePosition(opts.Position),
            opts.FontSize, opts.Opacity, opts.ColorHex);
    }

    public static void Draw(Bitmap bmp, string text, WatermarkPosition pos,
        int fontSize, int opacityPercent, string colorHex)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var sizePx = fontSize > 0 ? fontSize : WatermarkLayout.AutoFontSize(bmp.Width);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        using var font = new Font("Microsoft YaHei", sizePx, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(WithOpacity(ParseColor(colorHex), opacityPercent));
        var m = g.MeasureString(text, font);

        if (pos == WatermarkPosition.Tile)
        {
            var stepX = m.Width * TileStepXFactor;
            var stepY = m.Height * TileStepYFactor;
            foreach (var (x, y) in WatermarkLayout.Tile(bmp.Width, bmp.Height, stepX, stepY))
            {
                g.TranslateTransform(x, y);
                g.RotateTransform(TileAngle);
                g.DrawString(text, font, brush, 0, 0);
                g.ResetTransform();
            }
        }
        else
        {
            var (x, y) = WatermarkLayout.Anchor(bmp.Width, bmp.Height, m.Width, m.Height, pos);
            g.DrawString(text, font, brush, x, y);
        }
    }

    public static Color ParseColor(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
                return System.Drawing.ColorTranslator.FromHtml(hex.Trim());
        }
        catch (Exception) { /* fall through to default */ }
        return Color.FromArgb(0xFF, 0x3B, 0x30); // PixPin-like red
    }

    private static Color WithOpacity(Color c, int opacityPercent) =>
        Color.FromArgb(Math.Clamp(opacityPercent, 0, 100) * 255 / 100, c.R, c.G, c.B);
}
