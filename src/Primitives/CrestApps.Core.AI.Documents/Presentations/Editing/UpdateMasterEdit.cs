namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Changes a slide master or one of its layouts: its background, its title and body text styles, where its
/// placeholders sit, or its name.
/// </summary>
public sealed class UpdateMasterEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets what to change: <c>master</c> (every master), a master's name, or a layout's name or
    /// kind.
    /// </summary>
    public string Target { get; set; } = "master";

    /// <summary>
    /// Gets or sets the background.
    /// </summary>
    public PresentationBackgroundSpec Background { get; set; }

    /// <summary>
    /// Gets or sets the style of titles.
    /// </summary>
    public PresentationTextStyle TitleStyle { get; set; }

    /// <summary>
    /// Gets or sets the style of top-level body text.
    /// </summary>
    public PresentationTextStyle BodyStyle { get; set; }

    /// <summary>
    /// Gets or sets new positions for placeholders, keyed by placeholder role (<c>title</c>, <c>body</c>,
    /// <c>subtitle</c>, <c>footer</c>, <c>slide_number</c>, <c>date</c>).
    /// </summary>
    public IDictionary<string, PresentationBoundsSpec> PlaceholderBounds { get; set; } = new Dictionary<string, PresentationBoundsSpec>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets a new name for the master or layout.
    /// </summary>
    public string Rename { get; set; }
}
