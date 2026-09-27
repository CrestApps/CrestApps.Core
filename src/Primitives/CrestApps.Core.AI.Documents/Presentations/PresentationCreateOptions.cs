using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// How a new deck should be set up.
/// </summary>
public sealed class PresentationCreateOptions
{
    /// <summary>
    /// Gets or sets the slide width in EMUs.
    /// </summary>
    public long SlideWidth { get; set; } = PresentationUnits.WideSlideWidth;

    /// <summary>
    /// Gets or sets the slide height in EMUs.
    /// </summary>
    public long SlideHeight { get; set; } = PresentationUnits.WideSlideHeight;

    /// <summary>
    /// Gets or sets the theme's name.
    /// </summary>
    public string ThemeName { get; set; }

    /// <summary>
    /// Gets or sets theme colours to use instead of the defaults, keyed by slot or alias as
    /// <see cref="UpdateThemeEdit.Colors"/> accepts them.
    /// </summary>
    public IDictionary<string, string> ThemeColors { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the heading font.
    /// </summary>
    public string HeadingFont { get; set; }

    /// <summary>
    /// Gets or sets the body font.
    /// </summary>
    public string BodyFont { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether slides have a dark background with light text: the theme's
    /// dark colours become the backgrounds and its light colours the text.
    /// </summary>
    public bool DarkBackground { get; set; }

    /// <summary>
    /// Gets or sets the package of a deck or template to start from, so the new deck inherits its masters,
    /// layouts and theme. When set, the slide size and theme options are ignored.
    /// </summary>
    public byte[] TemplatePackage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the template's own slides are kept. By default a template
    /// contributes its design only.
    /// </summary>
    public bool KeepTemplateSlides { get; set; }

    /// <summary>
    /// Gets or sets the deck title recorded in its document properties.
    /// </summary>
    public string Title { get; set; }
}
