namespace Snapzy.Core.Capture;

/// <summary>
/// Where to place the post-selection floating toolbar relative to the
/// selection rectangle. All values are in overlay-canvas DIPs.
/// </summary>
public static class ToolbarPlacement
{
    public readonly record struct Pos(double X, double Y);

    /// <summary>
    /// Right-aligns the bar with the selection's right edge and puts it below
    /// the selection; flips above when there is no room below, and falls back
    /// to inside the selection's bottom edge when there is no room either way
    /// (e.g. full-screen selection). Always clamped inside the canvas.
    /// </summary>
    public static Pos Place(
        double selX, double selY, double selW, double selH,
        double barW, double barH,
        double canvasW, double canvasH,
        double margin = 8)
    {
        var x = selX + selW - barW;
        x = Math.Min(x, canvasW - barW - margin);
        x = Math.Max(x, margin);
        if (barW + 2 * margin > canvasW) x = Math.Max(0, (canvasW - barW) / 2);

        double y;
        if (selY + selH + margin + barH <= canvasH)
            y = selY + selH + margin;                                   // below
        else if (selY - margin - barH >= 0)
            y = selY - margin - barH;                                   // above
        else
            y = Math.Max(0, Math.Min(selY + selH, canvasH) - barH - margin); // inside bottom
        return new Pos(x, y);
    }
}
