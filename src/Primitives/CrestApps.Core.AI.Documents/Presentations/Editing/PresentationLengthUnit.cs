namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// The unit a <see cref="PresentationLength"/> is written in.
/// </summary>
public enum PresentationLengthUnit
{
    /// <summary>
    /// Points, 72 to the inch. A bare number is read as points.
    /// </summary>
    Points,

    /// <summary>
    /// Inches.
    /// </summary>
    Inches,

    /// <summary>
    /// Centimetres.
    /// </summary>
    Centimeters,

    /// <summary>
    /// Millimetres.
    /// </summary>
    Millimeters,

    /// <summary>
    /// Pixels at 96 dots per inch.
    /// </summary>
    Pixels,

    /// <summary>
    /// English Metric Units, the unit the package stores.
    /// </summary>
    Emus,

    /// <summary>
    /// A percentage of the slide's width (for horizontal lengths) or height (for vertical ones).
    /// </summary>
    Percent,
}
