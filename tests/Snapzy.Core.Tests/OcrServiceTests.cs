using System.Drawing;
using Snapzy.Core.Ocr;

public class OcrServiceTests
{
    [Fact]
    public async Task Recognize_RenderedText_FindsIt()
    {
        if (!OcrService.IsAvailable) return; // no OCR language installed - nothing to assert
        using var bmp = new Bitmap(400, 120);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.DrawString("HELLO SNAPZY 123", new Font("Arial", 24, FontStyle.Bold),
                Brushes.Black, new PointF(20, 40));
        }
        var text = await OcrService.RecognizeBitmapAsync(bmp);
        Assert.Contains("SNAPZY", text.ToUpperInvariant());
        Assert.Contains("123", text);
    }

    [Fact]
    public async Task RecognizeForClipboard_RenderedTable_ReturnsTsv()
    {
        if (!OcrService.IsAvailable) return;
        using var bmp = new Bitmap(560, 200);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            var font = new Font("Arial", 16, FontStyle.Regular);
            string[][] grid =
            {
                new[] { "Date", "Open", "Close" },
                new[] { "March", "1062", "1071" },
                new[] { "April", "1084", "1095" },
            };
            for (var r = 0; r < grid.Length; r++)
                for (var c = 0; c < grid[r].Length; c++)
                    g.DrawString(grid[r][c], font, Brushes.Black, new PointF(30 + c * 180, 30 + r * 50));
        }
        var result = await OcrService.RecognizeForClipboardAsync(bmp);
        Assert.True(result.IsTable);
        Assert.Equal(3, result.Rows);
        Assert.Equal(3, result.Columns);
        var rows = result.Text.Split('\n');
        Assert.Contains("\t", rows[0]);
        Assert.StartsWith("Date", rows[0]);
        Assert.Contains("1084", rows[2]);
    }

    [Fact]
    public async Task RecognizeForClipboard_PlainText_IsNotTable()
    {
        if (!OcrService.IsAvailable) return;
        using var bmp = new Bitmap(500, 120);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.DrawString("just one ordinary sentence here", new Font("Arial", 18), Brushes.Black, new PointF(20, 40));
        }
        var result = await OcrService.RecognizeForClipboardAsync(bmp);
        Assert.False(result.IsTable);
        Assert.DoesNotContain("\t", result.Text);
    }

    [Fact]
    public async Task RecognizeTable_ProseImage_StillReturnsTsvRows()
    {
        if (!OcrService.IsAvailable) return;
        using var bmp = new Bitmap(500, 160);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            var font = new Font("Arial", 18);
            g.DrawString("alpha beta", font, Brushes.Black, new PointF(20, 30));
            g.DrawString("gamma delta", font, Brushes.Black, new PointF(20, 90));
        }
        var result = await OcrService.RecognizeTableAsync(bmp);
        Assert.True(result.IsTable);        // forced mode always yields rows
        Assert.Equal(2, result.Rows);
        Assert.Contains("alpha", result.Text);
    }

    // The user-reported case: a ruled table whose cells hold single characters.
    // Whole-image OCR sees nothing here (engine drops isolated glyphs); the
    // grid path must still produce the full table.
    [Fact]
    public async Task RecognizeTable_BorderedSingleCharGrid_ReturnsAllCells()
    {
        if (!OcrService.IsAvailable) return;
        const int cellW = 180, cellH = 55, cols = 4, rows = 4, ox = 40, oy = 40;
        string[][] grid =
        {
            new[] { "1", "2", "3", "4" },
            new[] { "a", "b", "c", "d" },
            new[] { "e", "f", "g", "h" },
            new[] { "i", "j", "k", "l" },
        };
        using var bmp = new Bitmap(ox * 2 + cellW * cols, oy * 2 + cellH * rows);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            var pen = new Pen(Color.Black, 2);
            for (var r = 0; r <= rows; r++)
                g.DrawLine(pen, ox, oy + r * cellH, ox + cols * cellW, oy + r * cellH);
            for (var c = 0; c <= cols; c++)
                g.DrawLine(pen, ox + c * cellW, oy, ox + c * cellW, oy + rows * cellH);
            var font = new Font("Calibri", 20);
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    var sz = g.MeasureString(grid[r][c], font);
                    g.DrawString(grid[r][c], font, Brushes.Black,
                        ox + c * cellW + (cellW - sz.Width) / 2,
                        oy + r * cellH + (cellH - sz.Height) / 2);
                }
        }
        var result = await OcrService.RecognizeTableAsync(bmp);
        Assert.True(result.IsTable);
        Assert.Equal(4, result.Rows);
        Assert.Equal(4, result.Columns);
        var lines = result.Text.Split('\n');
        Assert.Equal(4, lines[1].Split('\t').Length);
        // Spot-check cells (OCR may vary case; letters like l/I are ambiguous)
        Assert.Contains("a", lines[1].Split('\t')[0].ToLowerInvariant());
        Assert.Contains("d", lines[1].Split('\t')[3].ToLowerInvariant());
        Assert.Contains("k", lines[3].Split('\t')[2].ToLowerInvariant());
    }

    [Fact]
    public async Task Recognize_ChineseText_UsesChineseCapableEngine()
    {
        if (!OcrService.IsAvailable) return;
        var hasZh = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages
            .Any(l => l.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        if (!hasZh) return; // nothing to assert without a Chinese OCR pack
        using var bmp = new Bitmap(560, 90);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            // "che xing yi gong chang" - vehicle type / plant one - plus Latin
            g.DrawString("车型 一工厂 SORP 2026", new Font("Microsoft YaHei", 22),
                Brushes.Black, new PointF(15, 20));
        }
        var text = await OcrService.RecognizeBitmapAsync(bmp);
        Assert.Contains("车型", text);      // CJK recognized AND not space-separated
        Assert.Contains("一工厂", text);
        Assert.Contains("SORP", text);      // Latin still works with the zh engine
        Assert.Contains("2026", text);
    }

    [Fact]
    public void GridDetector_FindsCellsInRuledGrid()
    {
        using var bmp = new Bitmap(400, 200);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            var pen = new Pen(Color.Black, 2);
            foreach (var y in new[] { 20, 80, 140 }) g.DrawLine(pen, 20, y, 380, y);
            foreach (var x in new[] { 20, 200, 380 }) g.DrawLine(pen, x, 20, x, 140);
        }
        var cells = GridDetector.FindCells(bmp);
        Assert.NotNull(cells);
        Assert.Equal(2, cells.Length);       // 2 rows
        Assert.Equal(2, cells[0].Length);    // 2 cols
        Assert.True(cells[0][0].Width > 150);
    }

    [Fact]
    public void GridDetector_PlainImage_ReturnsNull()
    {
        using var bmp = new Bitmap(300, 150);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.DrawString("no grid here", new Font("Arial", 16), Brushes.Black, new PointF(20, 40));
        }
        Assert.Null(GridDetector.FindCells(bmp));
    }

    [Fact]
    public async Task Recognize_BlankImage_ReturnsEmpty()
    {
        if (!OcrService.IsAvailable) return;
        using var bmp = new Bitmap(200, 100);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        Assert.Equal("", await OcrService.RecognizeBitmapAsync(bmp));
    }
}
