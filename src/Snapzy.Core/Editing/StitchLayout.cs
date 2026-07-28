namespace Snapzy.Core.Editing;

public enum StitchPlacement { Right, Bottom, Float }

/// <summary>Where an inserted image lands and how the canvas grows to hold it.</summary>
public record StitchResult(int X, int Y, int NewWidth, int NewHeight);

public static class StitchLayout
{
    public static StitchResult Place(int canvasW, int canvasH, int imgW, int imgH,
        StitchPlacement placement, int floatOffset = 24) => placement switch
    {
        StitchPlacement.Right => new StitchResult(
            canvasW, 0, canvasW + imgW, Math.Max(canvasH, imgH)),
        StitchPlacement.Bottom => new StitchResult(
            0, canvasH, Math.Max(canvasW, imgW), canvasH + imgH),
        _ => new StitchResult(
            floatOffset, floatOffset,
            Math.Max(canvasW, floatOffset + imgW),
            Math.Max(canvasH, floatOffset + imgH)),
    };
}
