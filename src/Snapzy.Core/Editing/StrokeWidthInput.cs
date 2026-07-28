using System.Globalization;

namespace Snapzy.Core.Editing;

public static class StrokeWidthInput
{
    public const double Min = 1;
    public const double Max = 64;

    /// <summary>Parses a user-typed stroke width, clamped to [1, 64]. False for non-numeric input.</summary>
    public static bool TryParse(string? text, out double width)
    {
        width = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) &&
            !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out v))
            return false;
        if (double.IsNaN(v) || double.IsInfinity(v)) return false;
        width = Math.Clamp(v, Min, Max);
        return true;
    }
}
