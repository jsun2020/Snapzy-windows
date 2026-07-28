using Snapzy.Core.Capture;
using Xunit;

namespace Snapzy.Core.Tests;

public class ToolbarPlacementTests
{
    private const double CanvasW = 1920;
    private const double CanvasH = 1080;
    private const double BarW = 260;
    private const double BarH = 40;

    [Fact]
    public void PlacesBelowSelection_RightAligned_WhenRoom()
    {
        var p = ToolbarPlacement.Place(100, 100, 400, 150, BarW, BarH, CanvasW, CanvasH);
        Assert.Equal(100 + 400 - BarW, p.X);
        Assert.Equal(100 + 150 + 8, p.Y);
    }

    [Fact]
    public void FlipsAbove_WhenNoRoomBelow()
    {
        var p = ToolbarPlacement.Place(100, 1000, 400, 60, BarW, BarH, CanvasW, CanvasH);
        Assert.Equal(1000 - 8 - BarH, p.Y);
    }

    [Fact]
    public void FallsInsideBottomEdge_ForFullScreenSelection()
    {
        var p = ToolbarPlacement.Place(0, 0, CanvasW, CanvasH, BarW, BarH, CanvasW, CanvasH);
        Assert.Equal(CanvasH - BarH - 8, p.Y);
        Assert.True(p.Y + BarH <= CanvasH);
    }

    [Fact]
    public void ClampsRight_WhenSelectionTouchesCanvasEdge()
    {
        var p = ToolbarPlacement.Place(CanvasW - 300, 100, 300, 100, BarW, BarH, CanvasW, CanvasH);
        Assert.Equal(CanvasW - BarW - 8, p.X);
    }

    [Fact]
    public void ClampsLeft_WhenSelectionNarrowAtLeftEdge()
    {
        var p = ToolbarPlacement.Place(0, 0, 50, 50, BarW, BarH, CanvasW, CanvasH);
        Assert.Equal(8, p.X);
    }

    [Fact]
    public void CentersHorizontally_WhenBarWiderThanCanvas()
    {
        var p = ToolbarPlacement.Place(0, 0, 100, 100, 500, BarH, 400, 300);
        Assert.Equal(0, p.X);
        Assert.True(p.Y >= 0);
    }
}
