using Snapzy.Core.Editing;

public class StitchLayoutTests
{
    [Fact]
    public void Right_AppendsAtOldEdge_WidensCanvas()
    {
        var r = StitchLayout.Place(800, 600, 400, 300, StitchPlacement.Right);
        Assert.Equal(new StitchResult(800, 0, 1200, 600), r);
    }

    [Fact]
    public void Right_TallerImage_GrowsHeightToo()
    {
        var r = StitchLayout.Place(800, 600, 400, 900, StitchPlacement.Right);
        Assert.Equal(new StitchResult(800, 0, 1200, 900), r);
    }

    [Fact]
    public void Bottom_AppendsBelow_GrowsHeight()
    {
        var r = StitchLayout.Place(800, 600, 1000, 300, StitchPlacement.Bottom);
        Assert.Equal(new StitchResult(0, 600, 1000, 900), r);
    }

    [Fact]
    public void Float_SmallImage_CanvasUnchanged()
    {
        var r = StitchLayout.Place(800, 600, 100, 100, StitchPlacement.Float);
        Assert.Equal(new StitchResult(24, 24, 800, 600), r);
    }

    [Fact]
    public void Float_LargeImage_CanvasGrowsToContainIt()
    {
        var r = StitchLayout.Place(800, 600, 900, 700, StitchPlacement.Float, floatOffset: 24);
        Assert.Equal(new StitchResult(24, 24, 924, 724), r);
    }
}
