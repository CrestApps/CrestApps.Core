using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using DocumentFormat.OpenXml;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Resolves DrawingML colours — fixed, theme, system and preset, with their tints, shades and luminance
/// changes — to the RGB value they are drawn in on one slide.
/// </summary>
/// <remarks>
/// A theme colour means different things on different slides: <c>bg1</c> is the theme's light colour on a
/// light slide and its dark colour on a slide whose colour map swaps them. The scope carries the theme and the
/// colour map of one slide, so every colour on it is read the way PowerPoint reads it there.
/// </remarks>
internal sealed class OpenXmlColorScope
{
    private static readonly string[] _colorElementNames = ["srgbClr", "schemeClr", "sysClr", "prstClr", "hslClr", "scrgbClr"];

    private readonly Dictionary<string, string> _colorMap;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenXmlColorScope"/> class.
    /// </summary>
    /// <param name="theme">The theme.</param>
    /// <param name="colorMap">The colour map, from logical names such as <c>bg1</c> to theme slots.</param>
    public OpenXmlColorScope(OpenXmlThemeInfo theme, Dictionary<string, string> colorMap)
    {
        Theme = theme;
        _colorMap = colorMap;
    }

    /// <summary>
    /// Gets the theme.
    /// </summary>
    public OpenXmlThemeInfo Theme { get; }

    /// <summary>
    /// Gets the theme colours keyed by slot, as the colour map arranges them for this slide.
    /// </summary>
    public Dictionary<string, string> EffectiveColors
    {
        get
        {
            var colors = new Dictionary<string, string>(Theme.Colors, StringComparer.OrdinalIgnoreCase);

            foreach (var (logical, slot) in _colorMap)
            {
                if (Theme.Colors.TryGetValue(slot, out var value))
                {
                    colors[logical] = value;
                }
            }

            return colors;
        }
    }

    /// <summary>
    /// Reads the colour map of a master and applies a slide's or layout's override on top of it.
    /// </summary>
    /// <param name="masterMap">The master's <c>p:clrMap</c>.</param>
    /// <param name="overrides">The <c>p:clrMapOvr</c> elements that apply, outermost last.</param>
    /// <returns>The effective map.</returns>
    public static Dictionary<string, string> ReadColorMap(OpenXmlElement masterMap, params OpenXmlElement[] overrides)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bg1"] = "lt1",
            ["tx1"] = "dk1",
            ["bg2"] = "lt2",
            ["tx2"] = "dk2",
        };

        Apply(map, masterMap);

        foreach (var overrideElement in overrides)
        {
            Apply(map, OpenXmlMarkup.Child(overrideElement, "overrideClrMapping"));
        }

        return map;
    }

    /// <summary>
    /// Resolves the first colour child of an element, such as the colour inside <c>a:solidFill</c>.
    /// </summary>
    /// <param name="parent">The element holding the colour.</param>
    /// <param name="placeholderColor">The colour <c>phClr</c> stands for, when a style is being applied.</param>
    /// <returns>The colour and its opacity, or <see langword="null"/> when there is no colour.</returns>
    public (string Hex, double Alpha)? ResolveChild(OpenXmlElement parent, string placeholderColor = null)
    {
        if (parent is null)
        {
            return null;
        }

        foreach (var child in parent.ChildElements)
        {
            if (Array.IndexOf(_colorElementNames, child.LocalName) >= 0)
            {
                return Resolve(child, placeholderColor);
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a colour element.
    /// </summary>
    /// <param name="color">The colour element.</param>
    /// <param name="placeholderColor">The colour <c>phClr</c> stands for, when a style is being applied.</param>
    /// <returns>The colour and its opacity.</returns>
    public (string Hex, double Alpha) Resolve(OpenXmlElement color, string placeholderColor = null)
    {
        var hex = color.LocalName switch
        {
            "srgbClr" => PresentationColor.NormalizeHex(OpenXmlMarkup.Attribute(color, "val")),
            "schemeClr" => ResolveScheme(OpenXmlMarkup.Attribute(color, "val"), placeholderColor),
            "sysClr" => PresentationColor.NormalizeHex(OpenXmlMarkup.Attribute(color, "lastClr"))
                ?? (OpenXmlMarkup.Attribute(color, "val") == "window" ? "FFFFFF" : "000000"),
            "prstClr" => PresentationColor.TryParse(OpenXmlMarkup.Attribute(color, "val"), out var preset) && !preset.IsTheme && !preset.IsNone
                ? preset.Hex
                : "000000",
            "hslClr" => FromHsl(color),
            "scrgbClr" => FromPercentages(color),
            _ => null,
        } ?? "000000";

        var alpha = 1d;
        double? luminanceModulation = null;
        double? luminanceOffset = null;

        foreach (var transform in color.ChildElements)
        {
            var value = OpenXmlMarkup.Long(transform, "val");

            if (value is null)
            {
                continue;
            }

            var fraction = value.Value / 100_000d;

            switch (transform.LocalName)
            {
                case "alpha":
                    alpha = Math.Clamp(fraction, 0, 1);
                    break;
                case "lumMod":
                    luminanceModulation = fraction;
                    break;
                case "lumOff":
                    luminanceOffset = fraction;
                    break;
                case "tint":
                    hex = PresentationColor.Tint(hex, fraction);
                    break;
                case "shade":
                    hex = PresentationColor.Darken(hex, fraction);
                    break;
            }
        }

        if (luminanceModulation is not null || luminanceOffset is not null)
        {
            hex = PresentationColor.ModulateLuminance(hex, luminanceModulation ?? 1, luminanceOffset ?? 0);
        }

        return (hex, alpha);
    }

    /// <summary>
    /// Resolves a scheme colour name such as <c>tx1</c> or <c>accent2</c> without transforms.
    /// </summary>
    /// <param name="name">The scheme colour name.</param>
    /// <param name="placeholderColor">The colour <c>phClr</c> stands for.</param>
    /// <returns>The colour as six hexadecimal digits.</returns>
    public string ResolveScheme(string name, string placeholderColor = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (name == "phClr")
        {
            return placeholderColor ?? Theme.Colors["accent1"];
        }

        var slot = _colorMap.TryGetValue(name, out var mapped) ? mapped : name;

        return Theme.Colors.TryGetValue(slot, out var hex) ? hex : null;
    }

    private static void Apply(Dictionary<string, string> map, OpenXmlElement mapping)
    {
        if (mapping is null || !mapping.HasAttributes)
        {
            return;
        }

        foreach (var attribute in mapping.GetAttributes())
        {
            if (string.IsNullOrEmpty(attribute.NamespaceUri) && !string.IsNullOrEmpty(attribute.Value))
            {
                map[attribute.LocalName] = attribute.Value;
            }
        }
    }

    private static string FromHsl(OpenXmlElement color)
    {
        var hue = (OpenXmlMarkup.Long(color, "hue") ?? 0) / 60_000d / 360d;
        var saturation = (OpenXmlMarkup.Long(color, "sat") ?? 0) / 100_000d;
        var luminance = (OpenXmlMarkup.Long(color, "lum") ?? 0) / 100_000d;

        // Built from a grey of the right luminance, then saturated by shading through the hue; close enough
        // for the rare deck that writes HSL, which PowerPoint itself never does.
        var gray = (int)Math.Round(Math.Clamp(luminance, 0, 1) * 255);
        var baseHex = string.Create(CultureInfo.InvariantCulture, $"{gray:X2}{gray:X2}{gray:X2}");

        return saturation <= 0 ? baseHex : PresentationColor.ModulateLuminance(HueToHex(hue), 1, (luminance - 0.5) * saturation);
    }

    private static string HueToHex(double hue)
    {
        var red = Channel(hue + (1d / 3));
        var green = Channel(hue);
        var blue = Channel(hue - (1d / 3));

        return string.Create(CultureInfo.InvariantCulture, $"{red:X2}{green:X2}{blue:X2}");

        static int Channel(double value)
        {
            value = value < 0 ? value + 1 : value > 1 ? value - 1 : value;

            var level = value < 1d / 6 ? value * 6 : value < 0.5 ? 1 : value < 2d / 3 ? (2d / 3 - value) * 6 : 0;

            return (int)Math.Round(level * 255);
        }
    }

    private static string FromPercentages(OpenXmlElement color)
    {
        static int Channel(long? value)
        {
            return (int)Math.Round(Math.Clamp((value ?? 0) / 100_000d, 0, 1) * 255);
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Channel(OpenXmlMarkup.Long(color, "r")):X2}{Channel(OpenXmlMarkup.Long(color, "g")):X2}{Channel(OpenXmlMarkup.Long(color, "b")):X2}");
    }
}
