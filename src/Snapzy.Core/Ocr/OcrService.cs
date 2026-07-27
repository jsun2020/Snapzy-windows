using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Snapzy.Core.Ocr;

public record OcrClipboardResult(string Text, bool IsTable, int Rows, int Columns);

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
        var (lines, _) = await RecognizeCoreAsync(bmp);
        return string.Join("\n", lines).Trim();
    }

    /// <summary>
    /// Clipboard-oriented recognition: tabular layouts (detected from word
    /// geometry) come back as tab-separated cells so they paste into
    /// spreadsheets as a table; everything else as plain lines.
    /// </summary>
    public static async Task<OcrClipboardResult> RecognizeForClipboardAsync(Bitmap bmp)
    {
        var (lines, words) = await RecognizeCoreAsync(bmp);
        var table = TableReconstructor.ToTable(words);
        if (table is not null)
            return new OcrClipboardResult(TableReconstructor.ToTsv(table), true, table.Length, table[0].Length);
        return new OcrClipboardResult(string.Join("\n", lines).Trim(), false, 0, 0);
    }

    /// <summary>
    /// Explicit table mode: strict geometric reconstruction when the layout
    /// qualifies, otherwise a loose rows/cells split - any recognized text
    /// comes back as TSV. Empty text means the engine found no words at all
    /// (e.g. isolated single-character cells, which Windows OCR cannot see).
    /// </summary>
    public static async Task<OcrClipboardResult> RecognizeTableAsync(Bitmap bmp)
    {
        var (_, words) = await RecognizeCoreAsync(bmp);
        var table = TableReconstructor.ToTable(words) ?? TableReconstructor.ToLooseTable(words);
        if (table is null) return new OcrClipboardResult("", false, 0, 0);
        return new OcrClipboardResult(
            TableReconstructor.ToTsv(table), true, table.Length, table.Max(r => r.Length));
    }

    private static async Task<(List<string> Lines, List<OcrWordBox> Words)> RecognizeCoreAsync(Bitmap bmp)
    {
        var engine = CreateEngine() ?? throw new InvalidOperationException("No OCR language available");
        var lines = new List<string>();
        var words = new List<OcrWordBox>();
        for (var top = 0; top < bmp.Height; top += SliceHeight - SliceOverlap)
        {
            var h = Math.Min(SliceHeight, bmp.Height - top);
            using var slice = bmp.Clone(new Rectangle(0, top, bmp.Width, h), PixelFormat.Format32bppArgb);
            var soft = ToSoftwareBitmap(slice);
            var result = await engine.RecognizeAsync(soft);
            foreach (var line in result.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.Text)) continue;
                lines.Add(line.Text);
                words.AddRange(line.Words.Select(w => new OcrWordBox(
                    w.Text, w.BoundingRect.X, w.BoundingRect.Y + top, w.BoundingRect.Width, w.BoundingRect.Height)));
            }
            if (h < SliceHeight) break;
        }
        return (lines, words);
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
