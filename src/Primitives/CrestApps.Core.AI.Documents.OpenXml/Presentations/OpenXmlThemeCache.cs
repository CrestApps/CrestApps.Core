using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Reads each master's theme once for a whole deck, along with the presentation's default text style.
/// </summary>
internal sealed class OpenXmlThemeCache
{
    private readonly Dictionary<Uri, OpenXmlThemeInfo> _themes = [];
    private readonly OpenXmlThemeInfo _fallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenXmlThemeCache"/> class.
    /// </summary>
    /// <param name="presentationPart">The presentation part.</param>
    public OpenXmlThemeCache(PresentationPart presentationPart)
    {
        DefaultTextStyle = OpenXmlMarkup.Child(presentationPart?.Presentation, "defaultTextStyle");
        _fallback = OpenXmlThemeInfo.Read(presentationPart?.ThemePart);
    }

    /// <summary>
    /// Gets the presentation's default text style.
    /// </summary>
    public OpenXmlElement DefaultTextStyle { get; }

    /// <summary>
    /// Returns the theme of a master.
    /// </summary>
    /// <param name="masterPart">The master, or <see langword="null"/> for the presentation's own theme.</param>
    /// <returns>The theme.</returns>
    public OpenXmlThemeInfo Get(SlideMasterPart masterPart)
    {
        var themePart = masterPart?.ThemePart;

        if (themePart is null)
        {
            return _fallback;
        }

        if (!_themes.TryGetValue(themePart.Uri, out var theme))
        {
            theme = OpenXmlThemeInfo.Read(themePart);
            _themes[themePart.Uri] = theme;
        }

        return theme;
    }

    /// <summary>
    /// Forgets every theme read so far, after an edit changed one.
    /// </summary>
    public void Clear()
    {
        _themes.Clear();
    }
}
