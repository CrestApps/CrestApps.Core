namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Changes the deck's theme colours and fonts, which every slide that uses theme colours and fonts follows.
/// </summary>
public sealed class UpdateThemeEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets new theme colours, keyed by slot (<c>dk1</c>, <c>lt1</c>, <c>dk2</c>, <c>lt2</c>,
    /// <c>accent1</c> to <c>accent6</c>, <c>hlink</c>, <c>folHlink</c>) or by an alias such as
    /// <c>text1</c> or <c>background2</c>.
    /// </summary>
    public IDictionary<string, string> Colors { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the heading font.
    /// </summary>
    public string HeadingFont { get; set; }

    /// <summary>
    /// Gets or sets the body font.
    /// </summary>
    public string BodyFont { get; set; }

    /// <summary>
    /// Gets or sets the theme's name.
    /// </summary>
    public string Name { get; set; }
}
