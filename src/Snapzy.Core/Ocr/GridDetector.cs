using System.Drawing;
using System.Drawing.Imaging;

namespace Snapzy.Core.Ocr;

/// <summary>
/// Detects a ruled table grid (drawn cell borders) in an image and returns the
/// cell rectangles. Pure image processing - no OCR.
/// </summary>
public static class GridDetector
{
    private const double LineCoverage = 0.55;  // dark fraction of span to count as a grid line
    private const byte DarkThreshold = 128;

    /// <summary>Cell rectangles as [row][col], or null when no ruled grid is found.</summary>
    public static Rectangle[][]? FindCells(Bitmap bmp)
    {
        var (rowDark, colDark, width, height) = CountDarkRuns(bmp);

        var hLines = ClusterLines(Enumerable.Range(0, height)
            .Where(y => rowDark[y] >= width * LineCoverage));
        var vLines = ClusterLines(Enumerable.Range(0, width)
            .Where(x => colDark[x] >= height * LineCoverage));

        // A grid needs at least 2x2 cells => 3 lines in each direction.
        if (hLines.Count < 3 || vLines.Count < 3) return null;

        var cells = new Rectangle[hLines.Count - 1][];
        for (var r = 0; r < hLines.Count - 1; r++)
        {
            cells[r] = new Rectangle[vLines.Count - 1];
            for (var c = 0; c < vLines.Count - 1; c++)
            {
                cells[r][c] = Rectangle.FromLTRB(
                    vLines[c].End, hLines[r].End, vLines[c + 1].Start, hLines[r + 1].Start);
                if (cells[r][c].Width < 4 || cells[r][c].Height < 4) return null;
            }
        }
        return cells;
    }

    /// <summary>
    /// Tight bounding box of dark (ink) pixels inside a region, or null when
    /// the region is blank. Used to crop cell CONTENT for composition.
    /// </summary>
    public static Rectangle? FindContentBounds(Bitmap bmp, Rectangle region)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (var y = Math.Max(0, region.Top); y < Math.Min(bmp.Height, region.Bottom); y++)
                {
                    var row = (byte*)data.Scan0 + y * data.Stride;
                    for (var x = Math.Max(0, region.Left); x < Math.Min(bmp.Width, region.Right); x++)
                    {
                        var lum = (row[x * 4 + 2] * 299 + row[x * 4 + 1] * 587 + row[x * 4] * 114) / 1000;
                        if (lum < DarkThreshold)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        if (maxX < 0) return null;
        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static (int[] RowDark, int[] ColDark, int Width, int Height) CountDarkRuns(Bitmap bmp)
    {
        var rowDark = new int[bmp.Height];
        var colDark = new int[bmp.Width];
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                for (var y = 0; y < bmp.Height; y++)
                {
                    var row = (byte*)data.Scan0 + y * data.Stride;
                    for (var x = 0; x < bmp.Width; x++)
                    {
                        var b = row[x * 4];
                        var g = row[x * 4 + 1];
                        var r = row[x * 4 + 2];
                        var lum = (r * 299 + g * 587 + b * 114) / 1000;
                        if (lum < DarkThreshold)
                        {
                            rowDark[y]++;
                            colDark[x]++;
                        }
                    }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return (rowDark, colDark, bmp.Width, bmp.Height);
    }

    /// <summary>Groups consecutive dark indices into (Start, End) line bands.</summary>
    private static List<(int Start, int End)> ClusterLines(IEnumerable<int> indices)
    {
        var lines = new List<(int Start, int End)>();
        int? start = null;
        var prev = -10;
        foreach (var i in indices)
        {
            if (start is null) { start = i; }
            else if (i - prev > 2)
            {
                lines.Add((start.Value, prev + 1));
                start = i;
            }
            prev = i;
        }
        if (start is not null) lines.Add((start.Value, prev + 1));
        return lines;
    }
}
