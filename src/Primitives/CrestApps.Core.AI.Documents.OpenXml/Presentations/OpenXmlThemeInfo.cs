using CrestApps.Core.AI.Documents.Presentations.Editing;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// The colours, fonts and style lists of a theme part, read once per master.
/// </summary>
internal sealed class OpenXmlThemeInfo
{
    /// <summary>
    /// Gets the theme's name.
    /// </summary>
    public string Name { get; private init; }

    /// <summary>
    /// Gets the colour scheme's name.
    /// </summary>
    public string ColorSchemeName { get; private init; }

    /// <summary>
    /// Gets the theme colours keyed by slot, each as six hexadecimal digits.
    /// </summary>
    public Dictionary<string, string> Colors { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the heading font.
    /// </summary>
    public string MajorFont { get; private init; } = "Calibri Light";

    /// <summary>
    /// Gets the body font.
    /// </summary>
    public string MinorFont { get; private init; } = "Calibri";

    /// <summary>
    /// Gets the fills a shape style refers to by index.
    /// </summary>
    public List<OpenXmlElement> FillStyles { get; } = [];

    /// <summary>
    /// Gets the outlines a shape style refers to by index.
    /// </summary>
    public List<OpenXmlElement> LineStyles { get; } = [];

    /// <summary>
    /// Gets the background fills a background reference refers to by index.
    /// </summary>
    public List<OpenXmlElement> BackgroundFillStyles { get; } = [];

    /// <summary>
    /// Reads a theme part.
    /// </summary>
    /// <param name="part">The theme part, or <see langword="null"/> for the defaults.</param>
    /// <returns>The theme.</returns>
    public static OpenXmlThemeInfo Read(ThemePart part)
    {
        var root = part?.Theme;
        var elements = OpenXmlMarkup.Child(root, "themeElements");
        var colorScheme = OpenXmlMarkup.Child(elements, "clrScheme");
        var fontScheme = OpenXmlMarkup.Child(elements, "fontScheme");
        var formatScheme = OpenXmlMarkup.Child(elements, "fmtScheme");

        var theme = new OpenXmlThemeInfo
        {
            Name = OpenXmlMarkup.Attribute(root, "name"),
            ColorSchemeName = OpenXmlMarkup.Attribute(colorScheme, "name"),
            MajorFont = Typeface(OpenXmlMarkup.Path(fontScheme, "majorFont", "latin")) ?? "Calibri Light",
            MinorFont = Typeface(OpenXmlMarkup.Path(fontScheme, "minorFont", "latin")) ?? "Calibri",
        };

        // Office defaults, so a theme missing a slot still resolves every colour a deck can name.
        string[] defaults = ["000000", "FFFFFF", "44546A", "E7E6E6", "4472C4", "ED7D31", "A5A5A5", "FFC000", "5B9BD5", "70AD47", "0563C1", "954F72"];
        string[] slots = ["dk1", "lt1", "dk2", "lt2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink"];

        for (var index = 0; index < slots.Length; index++)
        {
            theme.Colors[slots[index]] = ReadSchemeColor(OpenXmlMarkup.Child(colorScheme, slots[index])) ?? defaults[index];
        }

        theme.FillStyles.AddRange(OpenXmlMarkup.Child(formatScheme, "fillStyleLst")?.ChildElements ?? Enumerable.Empty<OpenXmlElement>());
        theme.LineStyles.AddRange(OpenXmlMarkup.Child(formatScheme, "lnStyleLst")?.ChildElements ?? Enumerable.Empty<OpenXmlElement>());
        theme.BackgroundFillStyles.AddRange(OpenXmlMarkup.Child(formatScheme, "bgFillStyleLst")?.ChildElements ?? Enumerable.Empty<OpenXmlElement>());

        return theme;
    }

    /// <summary>
    /// Replaces a theme font reference such as <c>+mj-lt</c> with the typeface it stands for.
    /// </summary>
    /// <param name="typeface">The typeface as the markup names it.</param>
    /// <returns>The actual typeface, or <see langword="null"/> when none is named.</returns>
    public string ResolveFont(string typeface)
    {
        if (string.IsNullOrWhiteSpace(typeface))
        {
            return null;
        }

        if (typeface.StartsWith("+mj", StringComparison.Ordinal))
        {
            return MajorFont;
        }

        if (typeface.StartsWith("+mn", StringComparison.Ordinal))
        {
            return MinorFont;
        }

        return typeface;
    }

    private static string Typeface(OpenXmlElement latin)
    {
        var value = OpenXmlMarkup.Attribute(latin, "typeface");

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string ReadSchemeColor(OpenXmlElement slot)
    {
        if (slot is null)
        {
            return null;
        }

        foreach (var child in slot.ChildElements)
        {
            switch (child.LocalName)
            {
                case "srgbClr":
                    return PresentationColor.NormalizeHex(OpenXmlMarkup.Attribute(child, "val"));

                case "sysClr":
                    return PresentationColor.NormalizeHex(OpenXmlMarkup.Attribute(child, "lastClr"))
                        ?? (OpenXmlMarkup.Attribute(child, "val") == "window" ? "FFFFFF" : "000000");
            }
        }

        return null;
    }
}
