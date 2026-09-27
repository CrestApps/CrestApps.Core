namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// What text looks like when nothing in its inheritance chain says otherwise: the shape style's font and
/// colour, a table style's header colour, or the application defaults.
/// </summary>
internal sealed class OpenXmlTextDefaults
{
    /// <summary>
    /// Gets or sets the typeface.
    /// </summary>
    public string Font { get; set; }

    /// <summary>
    /// Gets or sets the colour as six hexadecimal digits.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the size in points.
    /// </summary>
    public double Size { get; set; } = 18;

    /// <summary>
    /// Gets or sets whether text is bold.
    /// </summary>
    public bool Bold { get; set; }

    /// <summary>
    /// Gets or sets the colour of hyperlinked text.
    /// </summary>
    public string LinkColor { get; set; }

    /// <summary>
    /// Gets or sets the number of the slide the text is on, which a slide number field displays.
    /// </summary>
    public int? SlideNumber { get; set; }
}
