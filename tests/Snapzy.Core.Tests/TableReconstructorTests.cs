using Snapzy.Core.Ocr;

public class TableReconstructorTests
{
    // Word box helper: chars are ~10px wide, 16px tall.
    private static OcrWordBox W(string text, double x, double y) =>
        new(text, x, y, text.Length * 10, 16);

    [Fact]
    public void ToTable_SimpleGrid_ReturnsRowsAndColumns()
    {
        var words = new List<OcrWordBox>
        {
            W("Date", 10, 10),   W("Open", 200, 10),  W("Close", 400, 10),
            W("3/13", 10, 40),   W("0.06", 200, 40),  W("0.07", 400, 41),
            W("3/14", 10, 70),   W("0.08", 201, 70),  W("0.09", 400, 70),
        };
        var table = TableReconstructor.ToTable(words);
        Assert.NotNull(table);
        Assert.Equal(3, table.Length);
        Assert.Equal(new[] { "Date", "Open", "Close" }, table[0]);
        Assert.Equal(new[] { "3/13", "0.06", "0.07" }, table[1]);
        Assert.Equal(new[] { "3/14", "0.08", "0.09" }, table[2]);
    }

    [Fact]
    public void ToTable_MultiWordCell_StaysOneCell()
    {
        var words = new List<OcrWordBox>
        {
            W("Date", 10, 10),  W("Adj", 200, 10), W("Close", 245, 10), // 5px gap = same cell
            W("3/13", 10, 40),  W("0.062205", 200, 40),
            W("3/14", 10, 70),  W("0.064427", 200, 70),
        };
        var table = TableReconstructor.ToTable(words);
        Assert.NotNull(table);
        Assert.Equal(new[] { "Date", "Adj Close" }, table[0]);
        Assert.Equal(new[] { "3/13", "0.062205" }, table[1]);
    }

    [Fact]
    public void ToTable_MissingCell_BecomesEmpty()
    {
        var words = new List<OcrWordBox>
        {
            W("A", 10, 10),  W("B", 200, 10),  W("C", 400, 10),
            W("1", 10, 40),                    W("3", 400, 40), // middle cell absent
            W("4", 10, 70),  W("5", 200, 70),  W("6", 400, 70),
        };
        var table = TableReconstructor.ToTable(words);
        Assert.NotNull(table);
        Assert.Equal(new[] { "1", "", "3" }, table[1]);
    }

    [Fact]
    public void ToTable_ProseLines_ReturnsNull()
    {
        // Single-column paragraph text: word gaps are small everywhere.
        var words = new List<OcrWordBox>
        {
            W("This", 10, 10), W("is", 60, 10), W("plain", 95, 10), W("prose", 155, 10),
            W("with", 10, 40), W("no", 65, 40), W("columns", 100, 40),
        };
        Assert.Null(TableReconstructor.ToTable(words));
    }

    [Fact]
    public void ToTsv_JoinsCellsWithTabs()
    {
        var tsv = TableReconstructor.ToTsv(new[]
        {
            new[] { "Date", "Close" },
            new[] { "3/13", "0.06" },
        });
        Assert.Equal("Date\tClose\n3/13\t0.06", tsv);
    }
}
