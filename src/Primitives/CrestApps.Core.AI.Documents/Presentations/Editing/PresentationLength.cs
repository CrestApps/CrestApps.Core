using System.Globalization;

namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// A distance on a slide as a caller wrote it: a number and the unit it is in.
/// </summary>
/// <param name="Value">The number.</param>
/// <param name="Unit">The unit.</param>
public readonly record struct PresentationLength(double Value, PresentationLengthUnit Unit)
{
    // Longest suffixes first, so "emu" is not read as a length in "m" units that do not exist.
    private static readonly (string Suffix, PresentationLengthUnit Unit)[] _suffixes =
    [
        ("emus", PresentationLengthUnit.Emus),
        ("emu", PresentationLengthUnit.Emus),
        ("inches", PresentationLengthUnit.Inches),
        ("inch", PresentationLengthUnit.Inches),
        ("in", PresentationLengthUnit.Inches),
        ("\"", PresentationLengthUnit.Inches),
        ("cm", PresentationLengthUnit.Centimeters),
        ("mm", PresentationLengthUnit.Millimeters),
        ("px", PresentationLengthUnit.Pixels),
        ("pt", PresentationLengthUnit.Points),
        ("%", PresentationLengthUnit.Percent),
    ];

    /// <summary>
    /// Converts the length to EMUs.
    /// </summary>
    /// <param name="reference">
    /// The length a percentage is taken of: the slide width for a horizontal length, the slide height for a
    /// vertical one.
    /// </param>
    /// <returns>The length in EMUs.</returns>
    public long ToEmus(long reference)
    {
        var emus = Unit switch
        {
            PresentationLengthUnit.Inches => Value * PresentationUnits.EmusPerInch,
            PresentationLengthUnit.Centimeters => Value * PresentationUnits.EmusPerCentimeter,
            PresentationLengthUnit.Millimeters => Value * PresentationUnits.EmusPerMillimeter,
            PresentationLengthUnit.Pixels => Value * PresentationUnits.EmusPerPixel,
            PresentationLengthUnit.Emus => Value,
            PresentationLengthUnit.Percent => reference * Value / 100d,
            _ => Value * PresentationUnits.EmusPerPoint,
        };

        return (long)Math.Round(emus, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Creates a length in points.
    /// </summary>
    /// <param name="points">The number of points.</param>
    /// <returns>The length.</returns>
    public static PresentationLength FromPoints(double points)
    {
        return new PresentationLength(points, PresentationLengthUnit.Points);
    }

    /// <summary>
    /// Reads a length written as a number of points (<c>72</c>) or a number with a unit (<c>1in</c>,
    /// <c>2.5cm</c>, <c>20mm</c>, <c>96px</c>, <c>12pt</c>, <c>50%</c>).
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="length">The length, when the text is one.</param>
    /// <returns><see langword="true"/> when the text is a length.</returns>
    public static bool TryParse(string text, out PresentationLength length)
    {
        length = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim().ToLowerInvariant();
        var unit = PresentationLengthUnit.Points;
        var number = trimmed;

        foreach (var (suffix, candidate) in _suffixes)
        {
            if (trimmed.EndsWith(suffix, StringComparison.Ordinal))
            {
                unit = candidate;
                number = trimmed[..^suffix.Length].Trim();
                break;
            }
        }

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            double.IsNaN(value) ||
            double.IsInfinity(value))
        {
            return false;
        }

        length = new PresentationLength(value, unit);

        return true;
    }

    /// <summary>
    /// Writes the length back the way a caller would, for messages.
    /// </summary>
    /// <returns>The length, such as <c>1.5in</c>.</returns>
    public override string ToString()
    {
        var suffix = Unit switch
        {
            PresentationLengthUnit.Inches => "in",
            PresentationLengthUnit.Centimeters => "cm",
            PresentationLengthUnit.Millimeters => "mm",
            PresentationLengthUnit.Pixels => "px",
            PresentationLengthUnit.Emus => "emu",
            PresentationLengthUnit.Percent => "%",
            _ => "pt",
        };

        return Value.ToString("0.###", CultureInfo.InvariantCulture) + suffix;
    }
}
