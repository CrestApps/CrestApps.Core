using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// What a shape on one slide, layout or master inherits from: the layout and master behind it, their
/// placeholders and text styles, and the theme and colour map its colours are read through.
/// </summary>
/// <remarks>
/// The reader that builds the preview and the editor that shrinks text to fit both resolve through this one
/// class, so a title the editor measured at 40 points is the title the preview draws at 40 points.
/// </remarks>
internal sealed class OpenXmlSlideContext
{
    private readonly List<(OpenXmlPlaceholder Placeholder, OpenXmlElement Shape)> _layoutPlaceholders;
    private readonly List<(OpenXmlPlaceholder Placeholder, OpenXmlElement Shape)> _masterPlaceholders;

    private OpenXmlSlideContext(
        OpenXmlPart ownerPart,
        SlideLayoutPart layoutPart,
        SlideMasterPart masterPart,
        OpenXmlThemeInfo theme,
        OpenXmlColorScope colors,
        OpenXmlElement defaultTextStyle)
    {
        OwnerPart = ownerPart;
        LayoutPart = layoutPart;
        MasterPart = masterPart;
        Theme = theme;
        Colors = colors;
        DefaultTextStyle = defaultTextStyle;
        _layoutPlaceholders = ReadPlaceholders(layoutPart is not null && !ReferenceEquals(ownerPart, layoutPart) ? layoutPart.SlideLayout : null);
        _masterPlaceholders = ReadPlaceholders(masterPart is not null && !ReferenceEquals(ownerPart, masterPart) ? masterPart.SlideMaster : null);
    }

    /// <summary>
    /// Gets the part whose shapes are being read, which also resolves their relationships.
    /// </summary>
    public OpenXmlPart OwnerPart { get; }

    /// <summary>
    /// Gets the layout, when the owner is a slide or a layout.
    /// </summary>
    public SlideLayoutPart LayoutPart { get; }

    /// <summary>
    /// Gets the master.
    /// </summary>
    public SlideMasterPart MasterPart { get; }

    /// <summary>
    /// Gets the theme.
    /// </summary>
    public OpenXmlThemeInfo Theme { get; }

    /// <summary>
    /// Gets the colours as this part sees them.
    /// </summary>
    public OpenXmlColorScope Colors { get; }

    /// <summary>
    /// Gets the presentation's default text style.
    /// </summary>
    public OpenXmlElement DefaultTextStyle { get; }

    /// <summary>
    /// Gets a value indicating whether the owner is a slide rather than a layout or master.
    /// </summary>
    public bool IsSlide => OwnerPart is SlidePart;

    /// <summary>
    /// Creates the context of a slide.
    /// </summary>
    /// <param name="slidePart">The slide.</param>
    /// <param name="cache">The theme cache shared by every slide of the deck.</param>
    /// <returns>The context.</returns>
    public static OpenXmlSlideContext ForSlide(SlidePart slidePart, OpenXmlThemeCache cache)
    {
        var layoutPart = slidePart.SlideLayoutPart;
        var masterPart = layoutPart?.SlideMasterPart;
        var theme = cache.Get(masterPart);
        var colorMap = OpenXmlColorScope.ReadColorMap(
            OpenXmlMarkup.Child(masterPart?.SlideMaster, "clrMap"),
            OpenXmlMarkup.Child(layoutPart?.SlideLayout, "clrMapOvr"),
            OpenXmlMarkup.Child(slidePart.Slide, "clrMapOvr"));

        return new OpenXmlSlideContext(slidePart, layoutPart, masterPart, theme, new OpenXmlColorScope(theme, colorMap), cache.DefaultTextStyle);
    }

    /// <summary>
    /// Creates the context of a layout, for the shapes it draws on its slides.
    /// </summary>
    /// <param name="layoutPart">The layout.</param>
    /// <param name="cache">The theme cache.</param>
    /// <param name="slideColorMapOverride">The colour map override of the slide the shapes are drawn on.</param>
    /// <returns>The context.</returns>
    public static OpenXmlSlideContext ForLayout(SlideLayoutPart layoutPart, OpenXmlThemeCache cache, OpenXmlElement slideColorMapOverride = null)
    {
        var masterPart = layoutPart.SlideMasterPart;
        var theme = cache.Get(masterPart);
        var colorMap = OpenXmlColorScope.ReadColorMap(
            OpenXmlMarkup.Child(masterPart?.SlideMaster, "clrMap"),
            OpenXmlMarkup.Child(layoutPart.SlideLayout, "clrMapOvr"),
            slideColorMapOverride);

        return new OpenXmlSlideContext(layoutPart, layoutPart, masterPart, theme, new OpenXmlColorScope(theme, colorMap), cache.DefaultTextStyle);
    }

    /// <summary>
    /// Creates the context of a master, for the shapes it draws on its slides.
    /// </summary>
    /// <param name="masterPart">The master.</param>
    /// <param name="cache">The theme cache.</param>
    /// <param name="overrides">The colour map overrides of the layout and slide the shapes are drawn on.</param>
    /// <returns>The context.</returns>
    public static OpenXmlSlideContext ForMaster(SlideMasterPart masterPart, OpenXmlThemeCache cache, params OpenXmlElement[] overrides)
    {
        var theme = cache.Get(masterPart);
        var colorMap = OpenXmlColorScope.ReadColorMap(OpenXmlMarkup.Child(masterPart.SlideMaster, "clrMap"), overrides);

        return new OpenXmlSlideContext(masterPart, null, masterPart, theme, new OpenXmlColorScope(theme, colorMap), cache.DefaultTextStyle);
    }

    /// <summary>
    /// Finds the layout placeholder and master placeholder a placeholder inherits from.
    /// </summary>
    /// <param name="placeholder">The placeholder.</param>
    /// <returns>The layout shape and master shape, either of which may be missing.</returns>
    public (OpenXmlElement Layout, OpenXmlElement Master) FindInherited(OpenXmlPlaceholder placeholder)
    {
        var layout = Find(_layoutPlaceholders, placeholder);

        // The master is matched on the layout placeholder's type where there is one, because a slide
        // placeholder with only an index takes its kind from the layout.
        var masterKey = layout.Shape is null ? placeholder : layout.Placeholder;
        var master = FindMaster(masterKey);

        return (layout.Shape, master);
    }

    /// <summary>
    /// Returns the layout placeholders, for callers that fill new slides from them.
    /// </summary>
    /// <returns>The layout's placeholders and their shapes.</returns>
    public IReadOnlyList<(OpenXmlPlaceholder Placeholder, OpenXmlElement Shape)> LayoutPlaceholders()
    {
        return _layoutPlaceholders;
    }

    /// <summary>
    /// Returns a master text style: <c>titleStyle</c>, <c>bodyStyle</c> or <c>otherStyle</c>.
    /// </summary>
    /// <param name="name">The style's local name.</param>
    /// <returns>The style element, or <see langword="null"/>.</returns>
    public OpenXmlElement MasterTextStyle(string name)
    {
        return OpenXmlMarkup.Path(MasterPart?.SlideMaster, "txStyles", name);
    }

    /// <summary>
    /// Returns the paragraph property levels a paragraph at a level inherits from, most specific first.
    /// </summary>
    /// <param name="listStyles">The list styles to search, most specific first; missing ones are skipped.</param>
    /// <param name="level">The paragraph's level, from 0.</param>
    /// <returns>The <c>a:lvlNpPr</c> and <c>a:defPPr</c> elements, most specific first.</returns>
    public static List<OpenXmlElement> LevelChain(IEnumerable<OpenXmlElement> listStyles, int level)
    {
        var chain = new List<OpenXmlElement>();
        var name = "lvl" + (Math.Clamp(level, 0, 8) + 1).ToString(CultureInfo.InvariantCulture) + "pPr";

        foreach (var style in listStyles)
        {
            if (style is null)
            {
                continue;
            }

            var levelProperties = OpenXmlMarkup.Child(style, name);

            if (levelProperties is not null)
            {
                chain.Add(levelProperties);
            }

            var defaults = OpenXmlMarkup.Child(style, "defPPr");

            if (defaults is not null)
            {
                chain.Add(defaults);
            }
        }

        return chain;
    }

    /// <summary>
    /// Returns the list styles the text of a shape inherits from, most specific first.
    /// </summary>
    /// <param name="textBody">The shape's text body.</param>
    /// <param name="placeholder">The shape's placeholder, when it is one.</param>
    /// <returns>The list styles.</returns>
    public List<OpenXmlElement> ListStyles(OpenXmlElement textBody, OpenXmlPlaceholder? placeholder)
    {
        var styles = new List<OpenXmlElement> { OpenXmlMarkup.Child(textBody, "lstStyle") };

        if (placeholder is { } value)
        {
            var (layout, master) = FindInherited(value);

            styles.Add(OpenXmlMarkup.Path(layout, "txBody", "lstStyle"));
            styles.Add(OpenXmlMarkup.Path(master, "txBody", "lstStyle"));
            styles.Add(MasterTextStyle(value.TextStyleName));
        }
        else if (!IsSlide)
        {
            styles.Add(MasterTextStyle("otherStyle"));
        }

        styles.Add(DefaultTextStyle);

        return styles;
    }

    /// <summary>
    /// Returns the body properties a shape's text inherits from, most specific first.
    /// </summary>
    /// <param name="textBody">The shape's text body.</param>
    /// <param name="placeholder">The shape's placeholder, when it is one.</param>
    /// <returns>The <c>a:bodyPr</c> elements.</returns>
    public List<OpenXmlElement> BodyProperties(OpenXmlElement textBody, OpenXmlPlaceholder? placeholder)
    {
        var chain = new List<OpenXmlElement>();
        var own = OpenXmlMarkup.Child(textBody, "bodyPr");

        if (own is not null)
        {
            chain.Add(own);
        }

        if (placeholder is { } value)
        {
            var (layout, master) = FindInherited(value);
            var layoutBody = OpenXmlMarkup.Path(layout, "txBody", "bodyPr");
            var masterBody = OpenXmlMarkup.Path(master, "txBody", "bodyPr");

            if (layoutBody is not null)
            {
                chain.Add(layoutBody);
            }

            if (masterBody is not null)
            {
                chain.Add(masterBody);
            }
        }

        return chain;
    }

    /// <summary>
    /// Returns the transform a shape is positioned by: its own, or the one its placeholder inherits.
    /// </summary>
    /// <param name="element">The shape.</param>
    /// <returns>The <c>a:xfrm</c> or <c>p:xfrm</c> element, or <see langword="null"/>.</returns>
    public OpenXmlElement Transform(OpenXmlElement element)
    {
        var own = OwnTransform(element);

        if (own is not null)
        {
            return own;
        }

        if (OpenXmlPlaceholder.From(element) is not { } placeholder)
        {
            return null;
        }

        var (layout, master) = FindInherited(placeholder);

        return OwnTransform(layout) ?? OwnTransform(master);
    }

    /// <summary>
    /// Returns the transform written on a shape itself.
    /// </summary>
    /// <param name="element">The shape.</param>
    /// <returns>The transform element, or <see langword="null"/>.</returns>
    public static OpenXmlElement OwnTransform(OpenXmlElement element)
    {
        if (element is null)
        {
            return null;
        }

        var transform = element.LocalName switch
        {
            "graphicFrame" => OpenXmlMarkup.Child(element, "xfrm"),
            "grpSp" => OpenXmlMarkup.Path(element, "grpSpPr", "xfrm"),
            _ => OpenXmlMarkup.Path(element, "spPr", "xfrm"),
        };

        return transform is not null && OpenXmlMarkup.Child(transform, "ext") is not null ? transform : null;
    }

    private static List<(OpenXmlPlaceholder, OpenXmlElement)> ReadPlaceholders(OpenXmlElement root)
    {
        var placeholders = new List<(OpenXmlPlaceholder, OpenXmlElement)>();
        var tree = OpenXmlMarkup.Path(root, "cSld", "spTree");

        if (tree is null)
        {
            return placeholders;
        }

        foreach (var element in tree.Descendants())
        {
            if (element.LocalName is "sp" or "pic" or "graphicFrame" && OpenXmlPlaceholder.From(element) is { } placeholder)
            {
                placeholders.Add((placeholder, element));
            }
        }

        return placeholders;
    }

    private static (OpenXmlPlaceholder Placeholder, OpenXmlElement Shape) Find(
        List<(OpenXmlPlaceholder Placeholder, OpenXmlElement Shape)> candidates,
        OpenXmlPlaceholder placeholder)
    {
        foreach (var candidate in candidates)
        {
            if (placeholder.Matches(candidate.Placeholder, matchIndex: true))
            {
                return candidate;
            }
        }

        foreach (var candidate in candidates)
        {
            if (placeholder.Matches(candidate.Placeholder, matchIndex: false))
            {
                return candidate;
            }
        }

        return default;
    }

    private OpenXmlElement FindMaster(OpenXmlPlaceholder placeholder)
    {
        var type = placeholder.MasterType;

        foreach (var candidate in _masterPlaceholders)
        {
            if (candidate.Placeholder.MasterType == type)
            {
                return candidate.Shape;
            }
        }

        return null;
    }
}
