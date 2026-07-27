using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Snapzy.Core.Ocr;

public static class OcrService
{
    private const int SliceHeight = 2000; // OcrEngine.MaxImageDimension is 2600
    private const int SliceOverlap = 40;

    private static OcrEngine? CreateEngine() =>
        OcrEngine.TryCreateFromUserProfileLanguages()
        ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));

    public static bool IsAvailable => CreateEngine() is not null;

    public static async Task<string> RecognizeFileAsync(string imagePath)
    {
        using var bmp = new Bitmap(imagePath);
        return await RecognizeBitmapAsync(bmp);
    }

    public static async Task<string> RecognizeBitmapAsync(Bitmap bmp)
    {
        var engine = CreateEngine() ?? throw new InvalidOperationException("No OCR language available");
        var lines = new List<string>();
        for (var top = 0; top < bmp.Height; top += SliceHeight - SliceOverlap)
        {
            var h = Math.Min(SliceHeight, bmp.Height - top);
            using var slice = bmp.Clone(new Rectangle(0, top, bmp.Width, h), PixelFormat.Format32bppArgb);
            var soft = ToSoftwareBitmap(slice);
            var result = await engine.RecognizeAsync(soft);
            lines.AddRange(result.Lines.Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
            if (h < SliceHeight) break;
        }
        return string.Join("\n", lines).Trim();
    }

    private static SoftwareBitmap ToSoftwareBitmap(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            // Copy row-by-row into a tight buffer in case the stride is padded.
            var tight = new byte[bmp.Width * 4 * bmp.Height];
            for (var y = 0; y < bmp.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    data.Scan0 + y * data.Stride, tight, y * bmp.Width * 4, bmp.Width * 4);
            }
            var soft = new SoftwareBitmap(BitmapPixelFormat.Bgra8, bmp.Width, bmp.Height, BitmapAlphaMode.Premultiplied);
            soft.CopyFromBuffer(tight.AsBuffer());
            return soft;
        }
        finally { bmp.UnlockBits(data); }
    }
}
