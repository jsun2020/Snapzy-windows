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
    public async Task Recognize_BlankImage_ReturnsEmpty()
    {
        if (!OcrService.IsAvailable) return;
        using var bmp = new Bitmap(200, 100);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        Assert.Equal("", await OcrService.RecognizeBitmapAsync(bmp));
    }
}
