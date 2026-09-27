namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// A piece of a drawn line in one style.
/// </summary>
public sealed class SlideTextRun
{
    /// <summary>
    /// Gets or sets the text, as drawn.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the typeface.
    /// </summary>
    public string Font { get; set; }

    /// <summary>
    /// Gets or sets the size in points.
    /// </summary>
    public double Size { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is bold.
    /// </summary>
    public bool Bold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is italic.
    /// </summary>
    public bool Italic { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is underlined.
    /// </summary>
    public bool Underline { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is struck through.
    /// </summary>
    public bool Strikethrough { get; set; }

    /// <summary>
    /// Gets or sets the colour, as six hexadecimal digits.
    /// </summary>
    public string Color { get; set; } = "000000";

    /// <summary>
    /// Gets or sets the highlight colour behind the text, when there is one.
    /// </summary>
    public string Highlight { get; set; }

    /// <summary>
    /// Gets or sets the vertical offset as a percentage of the size: positive for superscript.
    /// </summary>
    public double Baseline { get; set; }

    /// <summary>
    /// Gets or sets the estimated width in points.
    /// </summary>
    public double Width { get; set; }
}
