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

    [Fact]
    public async Task Recognize_BlankImage_ReturnsEmpty()
    {
        if (!OcrService.IsAvailable) return;
        using var bmp = new Bitmap(200, 100);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        Assert.Equal("", await OcrService.RecognizeBitmapAsync(bmp));
    }
}
