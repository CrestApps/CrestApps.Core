namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// A ready-made set of theme colours and fonts a new deck can start from.
/// </summary>
public sealed class PresentationThemePreset
{
    /// <summary>
    /// Gets or sets the preset's name, as a caller asks for it.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a short description of the look.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the twelve theme colours, keyed by slot, each as six hexadecimal digits.
    /// </summary>
    public IReadOnlyDictionary<string, string> Colors { get; set; }

    /// <summary>
    /// Gets or sets the heading font.
    /// </summary>
    public string HeadingFont { get; set; }

    /// <summary>
    /// Gets or sets the body font.
    /// </summary>
    public string BodyFont { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the preset draws light text on dark slides.
    /// </summary>
    public bool DarkBackground { get; set; }
}
