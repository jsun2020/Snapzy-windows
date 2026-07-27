namespace Snapzy.Core.Ocr;

public record OcrWordBox(string Text, double X, double Y, double Width, double Height);

/// <summary>
/// Reconstructs tabular structure from OCR word bounding boxes: words are
/// grouped into visual rows, rows split into cells at large horizontal gaps,
/// and cell positions clustered into columns. Pure geometry - no OCR calls.
/// </summary>
public static class TableReconstructor
{
    // A gap wider than this many median-word-heights separates two cells;
    // narrower gaps are ordinary spaces inside one cell.
    private const double CellGapFactor = 1.5;
    // Cell left edges within this many median-word-heights belong to one column.
    private const double ColumnTolerance = 1.2;

    private sealed record Cell(string Text, double X);

    /// <summary>Returns rows of cell text, or null when the layout is not table-like.</summary>
    public static string[][]? ToTable(IReadOnlyList<OcrWordBox> words)
    {
        if (words.Count < 4) return null;
        var medianHeight = Median(words.Select(w => w.Height));
        if (medianHeight <= 0) return null;

        var rows = GroupIntoRows(words, medianHeight);
        if (rows.Count < 2) return null;

        var gapThreshold = medianHeight * CellGapFactor;
        var cellRows = rows.Select(r => SplitIntoCells(r, gapThreshold)).ToList();

        // Table heuristic: at least two rows must have split into 2+ cells.
        if (cellRows.Count(r => r.Count >= 2) < 2) return null;

        var columns = ClusterColumns(cellRows, medianHeight * ColumnTolerance);
        if (columns.Count < 2) return null;

        var result = new string[cellRows.Count][];
        for (var r = 0; r < cellRows.Count; r++)
        {
            var line = new string[columns.Count];
            Array.Fill(line, "");
            foreach (var cell in cellRows[r])
            {
                var col = NearestColumn(columns, cell.X);
                line[col] = line[col].Length == 0 ? cell.Text : line[col] + " " + cell.Text;
            }
            result[r] = line;
        }
        return result;
    }

    public static string ToTsv(string[][] table) =>
        string.Join("\n", table.Select(row => string.Join("\t", row)));

    /// <summary>
    /// Forced-table reconstruction: rows and gap-split cells without the
    /// "looks like a table" validation, so any recognized text becomes rows of
    /// cells. Used by the explicit Copy-Table action. Null only when no words.
    /// </summary>
    public static string[][]? ToLooseTable(IReadOnlyList<OcrWordBox> words)
    {
        if (words.Count == 0) return null;
        var medianHeight = Median(words.Select(w => w.Height));
        if (medianHeight <= 0) return null;
        var rows = GroupIntoRows(words, medianHeight);
        var gapThreshold = medianHeight * CellGapFactor;
        return rows
            .Select(r => SplitIntoCells(r, gapThreshold).Select(c => c.Text).ToArray())
            .ToArray();
    }

    private static List<List<OcrWordBox>> GroupIntoRows(IReadOnlyList<OcrWordBox> words, double medianHeight)
    {
        var rows = new List<List<OcrWordBox>>();
        foreach (var word in words.OrderBy(w => w.Y + w.Height / 2))
        {
            var centerY = word.Y + word.Height / 2;
            var row = rows.LastOrDefault(r =>
                Math.Abs(r.Average(w => w.Y + w.Height / 2) - centerY) < medianHeight * 0.6);
            if (row is null)
            {
                row = new List<OcrWordBox>();
                rows.Add(row);
            }
            row.Add(word);
        }
        foreach (var row in rows) row.Sort((a, b) => a.X.CompareTo(b.X));
        return rows;
    }

    private static List<Cell> SplitIntoCells(List<OcrWordBox> row, double gapThreshold)
    {
        var cells = new List<Cell>();
        var text = row[0].Text;
        var startX = row[0].X;
        for (var i = 1; i < row.Count; i++)
        {
            var gap = row[i].X - (row[i - 1].X + row[i - 1].Width);
            if (gap > gapThreshold)
            {
                cells.Add(new Cell(text, startX));
                text = row[i].Text;
                startX = row[i].X;
            }
            else
            {
                text += " " + row[i].Text;
            }
        }
        cells.Add(new Cell(text, startX));
        return cells;
    }

    private static List<double> ClusterColumns(List<List<Cell>> cellRows, double tolerance)
    {
        var lefts = cellRows.SelectMany(r => r).Select(c => c.X).OrderBy(x => x).ToList();
        var columns = new List<double>();
        var cluster = new List<double>();
        foreach (var x in lefts)
        {
            if (cluster.Count > 0 && x - cluster[^1] > tolerance)
            {
                columns.Add(cluster.Average());
                cluster.Clear();
            }
            cluster.Add(x);
        }
        if (cluster.Count > 0) columns.Add(cluster.Average());
        return columns;
    }

    private static int NearestColumn(List<double> columns, double x)
    {
        var best = 0;
        for (var i = 1; i < columns.Count; i++)
        {
            if (Math.Abs(columns[i] - x) < Math.Abs(columns[best] - x)) best = i;
        }
        return best;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }
}
