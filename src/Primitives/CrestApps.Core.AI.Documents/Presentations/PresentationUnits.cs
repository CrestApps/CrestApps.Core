using System.Globalization;

namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// Converts between the English Metric Units a presentation package stores and the units a reader thinks in.
/// </summary>
/// <remarks>
/// A deck measures everything in EMUs, 914,400 to the inch, so that inches and centimetres both divide it
/// evenly. Nobody asks for a box 2,743,200 EMUs wide, though, so every tool reports and accepts points — the
/// unit fonts are already sized in — and converts here, in one place, rather than each tool carrying its own
/// constants.
/// </remarks>
public static class PresentationUnits
{
    /// <summary>
    /// The number of EMUs in one point.
    /// </summary>
    public const long EmusPerPoint = 12_700;

    /// <summary>
    /// The number of EMUs in one inch.
    /// </summary>
    public const long EmusPerInch = 914_400;

    /// <summary>
    /// The number of EMUs in one centimetre.
    /// </summary>
    public const long EmusPerCentimeter = 360_000;

    /// <summary>
    /// The number of EMUs in one millimetre.
    /// </summary>
    public const long EmusPerMillimeter = 36_000;

    /// <summary>
    /// The number of EMUs in one pixel at 96 dots per inch.
    /// </summary>
    public const long EmusPerPixel = 9_525;

    /// <summary>
    /// The width of a 16:9 slide, the PowerPoint default, in EMUs.
    /// </summary>
    public const long WideSlideWidth = 12_192_000;

    /// <summary>
    /// The height of a 16:9 slide, the PowerPoint default, in EMUs.
    /// </summary>
    public const long WideSlideHeight = 6_858_000;

    /// <summary>
    /// Converts EMUs to points.
    /// </summary>
    /// <param name="emus">The length in EMUs.</param>
    /// <returns>The length in points.</returns>
    public static double ToPoints(long emus)
    {
        return emus / (double)EmusPerPoint;
    }

    /// <summary>
    /// Converts points to EMUs.
    /// </summary>
    /// <param name="points">The length in points.</param>
    /// <returns>The length in EMUs, rounded to the nearest whole unit.</returns>
    public static long FromPoints(double points)
    {
        return (long)Math.Round(points * EmusPerPoint, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Formats an EMU length as points for a tool response, with at most one decimal place.
    /// </summary>
    /// <param name="emus">The length in EMUs.</param>
    /// <returns>The formatted number of points.</returns>
    public static string FormatPoints(long emus)
    {
        return Math.Round(ToPoints(emus), 1).ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a number for a tool response, invariant and without trailing zeros.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The formatted number.</returns>
    public static string FormatNumber(double value)
    {
        return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
