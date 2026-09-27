namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// A stretch of text that shares one font, size and colour, with every inherited property resolved.
/// </summary>
public sealed class PresentationTextRun
{
    /// <summary>
    /// Gets or sets the text.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the typeface, with theme font references such as <c>+mj-lt</c> already replaced by the
    /// theme's heading or body font.
    /// </summary>
    public string Font { get; set; }

    /// <summary>
    /// Gets or sets the size in points, after any shrink-on-overflow scale is applied.
    /// </summary>
    public double Size { get; set; } = 18;

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
    /// Gets or sets the colour as six hexadecimal digits without a leading hash.
    /// </summary>
    public string Color { get; set; } = "000000";

    /// <summary>
    /// Gets or sets the highlight colour behind the text, when there is one.
    /// </summary>
    public string Highlight { get; set; }

    /// <summary>
    /// Gets or sets how the text is capitalised when drawn: <c>none</c>, <c>all</c> or <c>small</c>.
    /// </summary>
    public string Capitalization { get; set; } = "none";

    /// <summary>
    /// Gets or sets the vertical offset as a percentage of the font size: positive for superscript,
    /// negative for subscript, zero for neither.
    /// </summary>
    public double Baseline { get; set; }

    /// <summary>
    /// Gets or sets the link on this text, when it has one.
    /// </summary>
    public PresentationHyperlink Link { get; set; }

    /// <summary>
    /// Gets or sets the kind of field the run displays, such as <c>slidenum</c> or <c>datetime</c>, when it
    /// is filled in by the application rather than typed.
    /// </summary>
    public string FieldType { get; set; }

    /// <summary>
    /// Gets a value indicating whether the run ends the line it sits on, as a soft line break does.
    /// </summary>
    public bool IsLineBreak { get; set; }
}
