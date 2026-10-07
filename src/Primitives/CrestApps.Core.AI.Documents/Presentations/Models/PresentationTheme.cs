namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// The colours and fonts a deck's theme supplies.
/// </summary>
public sealed class PresentationTheme
{
    /// <summary>
    /// The theme colour slots, in the order PowerPoint lists them.
    /// </summary>
    public static readonly IReadOnlyList<string> ColorSlots =
    [
        "dk1",
        "lt1",
        "dk2",
        "lt2",
        "accent1",
        "accent2",
        "accent3",
        "accent4",
        "accent5",
        "accent6",
        "hlink",
        "folHlink",
    ];

    /// <summary>
    /// Gets or sets the theme's name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the name of the colour scheme.
    /// </summary>
    public string ColorSchemeName { get; set; }

    /// <summary>
    /// Gets or sets the theme colours, keyed by slot (see <see cref="ColorSlots"/>), each as six hexadecimal
    /// digits.
    /// </summary>
    public IDictionary<string, string> Colors { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the heading font, which titles use unless they set their own.
    /// </summary>
    public string HeadingFont { get; set; } = "Calibri Light";

    /// <summary>
    /// Gets or sets the body font, which all other text uses unless it sets its own.
    /// </summary>
    public string BodyFont { get; set; } = "Calibri";
}
