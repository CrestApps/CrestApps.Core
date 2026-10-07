using System.Globalization;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// Reads the design a document already follows — its body and heading typefaces, sizes and colors — so content
/// added to an uploaded document, and any style it lacks, match what is there rather than a default look.
/// </summary>
internal static class WordDesignReader
{
    /// <summary>
    /// Reads a document's design from its default formatting, its body style and its first heading style.
    /// </summary>
    /// <param name="mainPart">The main document part.</param>
    /// <returns>The design; anything the document does not say is taken from the default design.</returns>
    public static WordDesign Infer(MainDocumentPart mainPart)
    {
        var design = new WordDesign { Preset = "document" };

        if (mainPart?.StyleDefinitionsPart?.Styles is not { } styles)
        {
            return design;
        }

        var defaults = styles.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
        var normal = styles.Elements<Style>().FirstOrDefault(style => style.Type?.Value == StyleValues.Paragraph && style.Default?.Value == true)
            ?? styles.Elements<Style>().FirstOrDefault(style => string.Equals(style.StyleId?.Value, WordStyleSheet.Normal, StringComparison.Ordinal));

        var heading = styles.Elements<Style>().FirstOrDefault(style => WordStyleSheet.NamesMatch(style.StyleName?.Val?.Value, "heading 1"));
        var hyperlink = styles.Elements<Style>().FirstOrDefault(style => WordStyleSheet.NamesMatch(style.StyleName?.Val?.Value, "Hyperlink"));

        design.BodyFont = ReadFont(mainPart, normal?.StyleRunProperties?.RunFonts, minor: true)
            ?? ReadFont(mainPart, defaults?.RunFonts, minor: true)
            ?? design.BodyFont;

        design.HeadingFont = ReadFont(mainPart, heading?.StyleRunProperties?.RunFonts, minor: false)
            ?? ThemeFont(mainPart, minor: false)
            ?? design.BodyFont;

        if (TryReadSize(normal?.StyleRunProperties?.FontSize?.Val?.Value ?? defaults?.FontSize?.Val?.Value, out var size))
        {
            design.BodySize = size;
        }

        if (ReadColor(normal?.StyleRunProperties?.Color ?? defaults?.Color) is { } text)
        {
            design.TextColor = text;
        }

        if (ReadColor(heading?.StyleRunProperties?.Color) is { } headingColor)
        {
            design.HeadingColor = headingColor;
            design.AccentColor = headingColor;
            design.TableHeaderFill = headingColor;
        }

        if (ReadColor(hyperlink?.StyleRunProperties?.Color) is { } link)
        {
            design.LinkColor = link;
        }

        return design;
    }

    private static string ReadFont(MainDocumentPart mainPart, RunFonts fonts, bool minor)
    {
        if (fonts is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(fonts.Ascii?.Value))
        {
            return fonts.Ascii.Value;
        }

        if (fonts.AsciiTheme is not null)
        {
            var isMinor = fonts.AsciiTheme.Value == ThemeFontValues.MinorHighAnsi || fonts.AsciiTheme.Value == ThemeFontValues.MinorAscii;

            return ThemeFont(mainPart, isMinor);
        }

        return minor ? null : ThemeFont(mainPart, minor);
    }

    /// <summary>
    /// Reads the body (minor) or heading (major) typeface of a document's theme.
    /// </summary>
    /// <param name="mainPart">The main document part.</param>
    /// <param name="minor">Whether the body typeface is wanted rather than the heading typeface.</param>
    /// <returns>The typeface, or <see langword="null"/> when the document has no theme.</returns>
    public static string ThemeFont(MainDocumentPart mainPart, bool minor)
    {
        var scheme = mainPart?.ThemePart?.Theme?.ThemeElements?.FontScheme;
        Drawing.FontCollectionType collection = minor ? scheme?.MinorFont : scheme?.MajorFont;
        var typeface = collection?.LatinFont?.Typeface?.Value;

        return string.IsNullOrWhiteSpace(typeface) ? null : typeface;
    }

    private static bool TryReadSize(string halfPoints, out double size)
    {
        size = 0;

        if (!int.TryParse(halfPoints, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return false;
        }

        size = value / 2d;

        return true;
    }

    private static string ReadColor(Color color)
    {
        var value = color?.Val?.Value;

        return string.IsNullOrWhiteSpace(value) || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase) ? null : value.ToUpperInvariant();
    }
}
