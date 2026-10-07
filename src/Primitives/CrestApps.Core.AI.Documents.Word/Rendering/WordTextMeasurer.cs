namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// Estimates how wide and tall text is set, without a font to measure against.
/// </summary>
/// <remarks>
/// Nothing in this process can load a typeface, and the preview is drawn by whatever browser shows it, so widths
/// are the Helvetica metrics scaled by how wide the named typeface runs, and line heights are the typeface's
/// usual line spacing. They decide where a line breaks and where a page ends; because the preview, the PDF export
/// and the page numbers of a table of contents all ask this one class, they agree with each other even where the
/// estimate is a little off from Word.
/// </remarks>
internal static class WordTextMeasurer
{
    // Helvetica advance widths for the printable ASCII characters 32-126, in thousandths of an em.
    private static readonly short[] _ascii =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private static readonly Dictionary<string, (double Width, double Line)> _fonts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Calibri"] = (0.90, 1.22),
        ["Calibri Light"] = (0.88, 1.22),
        ["Carlito"] = (0.90, 1.22),
        ["Aptos"] = (0.95, 1.20),
        ["Aptos Display"] = (0.93, 1.20),
        ["Arial"] = (1.00, 1.15),
        ["Helvetica"] = (1.00, 1.15),
        ["Liberation Sans"] = (1.00, 1.15),
        ["Arial Narrow"] = (0.82, 1.15),
        ["Arial Black"] = (1.20, 1.41),
        ["Segoe UI"] = (0.98, 1.33),
        ["Segoe UI Light"] = (0.95, 1.33),
        ["Segoe UI Semibold"] = (1.01, 1.33),
        ["Verdana"] = (1.14, 1.22),
        ["Tahoma"] = (0.98, 1.21),
        ["Trebuchet MS"] = (0.97, 1.16),
        ["Century Gothic"] = (1.08, 1.23),
        ["Gill Sans MT"] = (0.91, 1.15),
        ["Franklin Gothic Book"] = (0.92, 1.13),
        ["Corbel"] = (0.91, 1.22),
        ["Candara"] = (0.92, 1.22),
        ["Lato"] = (0.97, 1.20),
        ["Open Sans"] = (1.03, 1.36),
        ["Roboto"] = (0.97, 1.17),
        ["Montserrat"] = (1.08, 1.22),
        ["Georgia"] = (1.04, 1.14),
        ["Cambria"] = (0.96, 1.17),
        ["Caladea"] = (0.96, 1.17),
        ["Constantia"] = (0.97, 1.22),
        ["Times New Roman"] = (0.89, 1.15),
        ["Times"] = (0.89, 1.15),
        ["Liberation Serif"] = (0.89, 1.15),
        ["Garamond"] = (0.86, 1.12),
        ["Book Antiqua"] = (0.96, 1.17),
        ["Palatino Linotype"] = (0.96, 1.17),
    };

    /// <summary>
    /// Estimates the width of text in points.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="font">The typeface.</param>
    /// <param name="size">The size in points.</param>
    /// <param name="bold">Whether the text is bold.</param>
    /// <returns>The width in points.</returns>
    public static double Measure(string text, string font, double size, bool bold)
    {
        if (string.IsNullOrEmpty(text) || size <= 0)
        {
            return 0;
        }

        if (IsMonospace(font))
        {
            return text.Length * 0.6 * size;
        }

        var units = 0d;

        foreach (var character in text)
        {
            units += CharacterWidth(character);
        }

        var factor = _fonts.TryGetValue(font ?? string.Empty, out var metrics) ? metrics.Width : 0.95;

        return units / 1000d * size * factor * (bold ? 1.06 : 1);
    }

    /// <summary>
    /// Returns the height of one single-spaced line of a typeface, in points.
    /// </summary>
    /// <param name="font">The typeface.</param>
    /// <param name="size">The size in points.</param>
    /// <returns>The line height.</returns>
    public static double LineHeight(string font, double size)
    {
        var factor = _fonts.TryGetValue(font ?? string.Empty, out var metrics) ? metrics.Line : IsMonospace(font) ? 1.17 : 1.18;

        return size * factor;
    }

    /// <summary>
    /// Returns how far below the top of a single-spaced line the baseline sits, in points.
    /// </summary>
    /// <param name="font">The typeface.</param>
    /// <param name="size">The size in points.</param>
    /// <returns>The ascent.</returns>
    public static double Ascent(string font, double size)
    {
        return LineHeight(font, size) - (size * 0.24);
    }

    /// <summary>
    /// Returns whether a typeface sets every character at the same width.
    /// </summary>
    /// <param name="font">The typeface.</param>
    /// <returns><see langword="true"/> for a monospaced typeface.</returns>
    public static bool IsMonospace(string font)
    {
        return font is not null &&
            (font.Contains("Consolas", StringComparison.OrdinalIgnoreCase) ||
             font.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
             font.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
             font.Contains("Lucida Console", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns whether a typeface is one this host's previews and PDFs draw as itself or a metric-compatible
    /// substitute, rather than a generic fallback.
    /// </summary>
    /// <param name="font">The typeface.</param>
    /// <returns><see langword="true"/> for a known typeface.</returns>
    public static bool IsKnown(string font)
    {
        return font is not null && (_fonts.ContainsKey(font) || IsMonospace(font));
    }

    private static double CharacterWidth(char character)
    {
        if (character >= 32 && character <= 126)
        {
            return _ascii[character - 32];
        }

        if (character == ' ')
        {
            return 278;
        }

        if (character is '–')
        {
            return 556;
        }

        if (character is '—')
        {
            return 1000;
        }

        if (character is '‘' or '’' or '‚')
        {
            return 222;
        }

        if (character is '“' or '”' or '„')
        {
            return 333;
        }

        if (character is '•' or '◦' or '▪')
        {
            return 350;
        }

        if (character == '…')
        {
            return 1000;
        }

        // East Asian scripts are set on a square em.
        if (character >= '⺀' && character <= '￯')
        {
            return 1000;
        }

        return char.IsUpper(character) ? 690 : 556;
    }
}
