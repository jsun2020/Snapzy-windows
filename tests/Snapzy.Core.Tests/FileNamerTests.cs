using Snapzy.Core.History;

public class FileNamerTests
{
    [Fact]
    public void NewCaptureName_FormatsTimestamp()
    {
        var name = FileNamer.NewCaptureName(new DateTime(2026, 7, 26, 14, 3, 5), "png");
        Assert.Equal("Snapzy 2026-07-26 14.03.05.png", name);
    }
}
