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
        // Ruled grid first: cell-by-cell recognition sees content (even single
        // characters) that whole-image OCR misses entirely.
        var cells = GridDetector.FindCells(bmp);
        if (cells is not null)
        {
            var grid = await RecognizeGridAsync(bmp, cells);
            if (grid.Any(row => row.Any(c => c.Length > 0)))
                return new OcrClipboardResult(
                    TableReconstructor.ToTsv(grid), true, grid.Length, grid.Max(r => r.Length));
        }

        var (_, words) = await RecognizeCoreAsync(bmp);
        var table = TableReconstructor.ToTable(words) ?? TableReconstructor.ToLooseTable(words);
        if (table is null) return new OcrClipboardResult("", false, 0, 0);
        return new OcrClipboardResult(
            TableReconstructor.ToTsv(table), true, table.Length, table.Max(r => r.Length));
    }

    /// <summary>
    /// OCRs a ruled grid row by row: the cells of a row are composed side by
    /// side into one compact strip (the engine ignores isolated glyphs, but
    /// recognizes them with neighbors at moderate gaps), then each recognized
    /// word maps back to its cell via the known segment offsets.
    /// </summary>
    private static async Task<string[][]> RecognizeGridAsync(Bitmap bmp, Rectangle[][] cells)
    {
        const int inset = 3;      // stay clear of the border lines
        const int margin = 24;    // white margin around the strip
        var result = new string[cells.Length][];
        for (var r = 0; r < cells.Length; r++)
        {
            var row = cells[r];

            // Tight content crops: composing whole cells would keep the glyphs
            // as far apart as in the original (the engine would drop them again).
            var contents = new Rectangle?[row.Length];
            for (var c = 0; c < row.Length; c++)
            {
                var src = row[c];
                src.Inflate(-inset, -inset);
                contents[c] = src.Width > 0 && src.Height > 0
                    ? GridDetector.FindContentBounds(bmp, src)
                    : null;
            }
            // Compose at a normalized glyph height with a SMALL gap: neighbors
            // make the engine see every glyph (it may fuse cells into one
            // word, which is fine - characters are mapped back individually).
            const int normHeight = 32;
            const int gap = 12;
            var segStarts = new int[row.Length];
            var segWidths = new int[row.Length];
            var x = margin;
            for (var c = 0; c < row.Length; c++)
            {
                segStarts[c] = x;
                var w = contents[c] is { } b
                    ? Math.Max(4, (int)Math.Round(b.Width * (double)normHeight / Math.Max(1, b.Height)))
                    : 8;
                segWidths[c] = w;
                x += w + gap;
            }
            using var strip = new Bitmap(x - gap + margin, normHeight + margin * 2, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(strip))
            {
                g.Clear(Color.White);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                for (var c = 0; c < row.Length; c++)
                {
                    if (contents[c] is not { } src) continue;
                    g.DrawImage(bmp,
                        new Rectangle(segStarts[c], margin, segWidths[c], normHeight),
                        src, GraphicsUnit.Pixel);
                }
            }

            var (_, words) = await RecognizeCoreAsync(strip);
            var cellText = new string[row.Length];
            Array.Fill(cellText, "");
            foreach (var word in words.OrderBy(w => w.X))
            {
                // Assign character-by-character: interpolate each character's
                // center across the word's bounding box, pick its segment.
                var chars = word.Text;
                for (var i = 0; i < chars.Length; i++)
                {
                    var charCenter = word.X + word.Width * (i + 0.5) / chars.Length;
                    var seg = 0;
                    var bestDist = double.MaxValue;
                    for (var c = 0; c < row.Length; c++)
                    {
                        var segCenter = segStarts[c] + segWidths[c] / 2.0;
                        var dist = Math.Abs(charCenter - segCenter);
                        // Inside the segment always wins; otherwise nearest center.
                        if (charCenter >= segStarts[c] && charCenter <= segStarts[c] + segWidths[c]) { seg = c; break; }
                        if (dist < bestDist) { bestDist = dist; seg = c; }
                    }
                    cellText[seg] += chars[i];
                }
            }
            result[r] = cellText.Select(t => t.Trim()).ToArray();
        }
        return result;
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
