using Snapzy.Core.Editing;

public class StrokeWidthInputTests
{
    [Theory]
    [InlineData("3", 3)]
    [InlineData(" 12 ", 12)]
    [InlineData("2.5", 2.5)]
    [InlineData("0", 1)]      // clamped up
    [InlineData("999", 64)]   // clamped down
    [InlineData("-4", 1)]
    public void TryParse_Numeric_ClampsToRange(string text, double expected)
    {
        Assert.True(StrokeWidthInput.TryParse(text, out var w));
        Assert.Equal(expected, w);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData(null)]
    [InlineData("NaN")]
    public void TryParse_Invalid_ReturnsFalse(string? text)
    {
        Assert.False(StrokeWidthInput.TryParse(text, out _));
    }
}
