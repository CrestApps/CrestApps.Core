using System.Globalization;

namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// Converts between the units a word-processing document is measured in: points, twentieths of a point
/// (twips, used by page and paragraph measurements), half-points (used by font sizes) and English Metric
/// Units (used by drawings).
/// </summary>
internal static class WordUnits
{
    /// <summary>
    /// Twentieths of a point in one point.
    /// </summary>
    public const int TwipsPerPoint = 20;

    /// <summary>
    /// English Metric Units in one point.
    /// </summary>
    public const long EmusPerPoint = 12_700;

    /// <summary>
    /// Points in one inch.
    /// </summary>
    public const double PointsPerInch = 72;

    /// <summary>
    /// Converts points to twips, rounding to the nearest whole twip.
    /// </summary>
    /// <param name="points">The length in points.</param>
    /// <returns>The length in twips.</returns>
    public static int ToTwips(double points)
    {
        return (int)Math.Round(points * TwipsPerPoint, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Converts twips to points.
    /// </summary>
    /// <param name="twips">The length in twips.</param>
    /// <returns>The length in points.</returns>
    public static double FromTwips(double twips)
    {
        return twips / TwipsPerPoint;
    }

    /// <summary>
    /// Converts points to English Metric Units.
    /// </summary>
    /// <param name="points">The length in points.</param>
    /// <returns>The length in EMUs.</returns>
    public static long ToEmus(double points)
    {
        return (long)Math.Round(points * EmusPerPoint, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Converts English Metric Units to points.
    /// </summary>
    /// <param name="emus">The length in EMUs.</param>
    /// <returns>The length in points.</returns>
    public static double FromEmus(double emus)
    {
        return emus / EmusPerPoint;
    }

    /// <summary>
    /// Formats a font size in points as the half-point value a run property stores.
    /// </summary>
    /// <param name="points">The size in points.</param>
    /// <returns>The size in half-points, as text.</returns>
    public static string ToHalfPoints(double points)
    {
        return ((int)Math.Round(points * 2, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a whole number as invariant text, the way every numeric attribute is written.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The value as text.</returns>
    public static string Invariant(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads a length a model wrote, such as <c>1in</c>, <c>2.5cm</c>, <c>10mm</c>, <c>12pt</c>, <c>96px</c> or
    /// <c>50%</c>. A bare number is read as points.
    /// </summary>
    /// <param name="value">The length.</param>
    /// <param name="referencePoints">What a percentage is a percentage of, in points.</param>
    /// <param name="points">The length in points.</param>
    /// <returns><see langword="true"/> when the value is a length.</returns>
    public static bool TryParseLength(string value, double referencePoints, out double points)
    {
        points = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        var unitStart = text.Length;

        while (unitStart > 0 && (char.IsAsciiLetter(text[unitStart - 1]) || text[unitStart - 1] == '%'))
        {
            unitStart--;
        }

        if (!double.TryParse(text.AsSpan(0, unitStart), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        var factor = text[unitStart..] switch
        {
            "" or "pt" or "pts" or "point" or "points" => 1d,
            "in" or "inch" or "inches" or "\"" => PointsPerInch,
            "cm" => PointsPerInch / 2.54,
            "mm" => PointsPerInch / 25.4,
            "px" => 0.75,
            "pc" or "pica" => 12d,
            "%" => referencePoints / 100,
            _ => double.NaN,
        };

        if (double.IsNaN(factor))
        {
            return false;
        }

        points = number * factor;

        return double.IsFinite(points);
    }
}
