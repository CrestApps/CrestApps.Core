namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// Estimates how wide text is drawn, without a font to measure against.
/// </summary>
/// <remarks>
/// Nothing in this process can load a typeface, and the preview is drawn by whatever browser shows it, so the
/// widths are per-character approximations scaled by how wide the named typeface runs. They decide where a
/// line breaks and whether text overflows its box — and because the engine that shrinks text to fit, the
/// preview and the PDF export all ask this one class, they agree on both even where the estimate is a little
/// off from the real font.
/// </remarks>
internal static class PresentationTextMeasurer
{
    private static readonly Dictionary<string, double> _fontFactors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Calibri"] = 0.9,
        ["Calibri Light"] = 0.88,
        ["Carlito"] = 0.9,
        ["Aptos"] = 0.96,
        ["Aptos Display"] = 0.94,
        ["Segoe UI"] = 1,
        ["Segoe UI Light"] = 0.97,
        ["Segoe UI Semibold"] = 1.03,
        ["Arial"] = 1.02,
        ["Arial Black"] = 1.22,
        ["Helvetica"] = 1.02,
        ["Helvetica Neue"] = 1.0,
        ["Liberation Sans"] = 1.02,
        ["Verdana"] = 1.16,
        ["Tahoma"] = 1.0,
        ["Trebuchet MS"] = 1.0,
        ["Century Gothic"] = 1.1,
        ["Gill Sans MT"] = 0.93,
        ["Franklin Gothic Medium"] = 0.95,
        ["Franklin Gothic Book"] = 0.93,
        ["Corbel"] = 0.92,
        ["Candara"] = 0.93,
        ["Lato"] = 0.98,
        ["Open Sans"] = 1.04,
        ["Roboto"] = 0.98,
        ["Montserrat"] = 1.1,
        ["Georgia"] = 1.06,
        ["Cambria"] = 0.97,
        ["Constantia"] = 0.98,
        ["Times New Roman"] = 0.92,
        ["Garamond"] = 0.88,
        ["Book Antiqua"] = 0.98,
        ["Palatino Linotype"] = 0.98,
        ["Consolas"] = 1.0,
        ["Courier New"] = 1.07,
    };

    /// <summary>
    /// Estimates the width of a string in points.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="font">The typeface.</param>
    /// <param name="size">The size in points.</param>
    /// <param name="bold">Whether the text is bold.</param>
    /// <returns>The estimated width in points.</returns>
    public static double Measure(string text, string font, double size, bool bold)
    {
        if (string.IsNullOrEmpty(text))
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

        return units * size * FontFactor(font) * (bold ? 1.05 : 1);
    }

    /// <summary>
    /// Returns how wide a typeface runs relative to a typical UI sans-serif.
    /// </summary>
    /// <param name="font">The typeface.</param>
    /// <returns>The factor, where 1 is typical.</returns>
    public static double FontFactor(string font)
    {
        if (string.IsNullOrWhiteSpace(font))
        {
            return 0.92;
        }

        return _fontFactors.TryGetValue(font.Trim(), out var factor) ? factor : 1;
    }

    private static bool IsMonospace(string font)
    {
        return font is not null &&
            (font.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
            font.Contains("Consolas", StringComparison.OrdinalIgnoreCase) ||
            font.Contains("Courier", StringComparison.OrdinalIgnoreCase));
    }

    private static double CharacterWidth(char character)
    {
        if (char.IsAsciiDigit(character))
        {
            return 0.55;
        }

        if (character > 0x2E80)
        {
            // CJK ideographs and full-width forms take a full em.
            return 1;
        }

        return character switch
        {
            ' ' => 0.26,
            'i' or 'j' or 'l' or 'I' or '.' or ',' or ':' or ';' or '\'' or '|' or '!' or '`' => 0.26,
            'f' or 't' or 'r' or '(' or ')' or '[' or ']' or '{' or '}' or '/' or '\\' or '-' => 0.34,
            'm' or 'w' => 0.82,
            'M' or 'W' => 0.9,
            '@' or '%' => 0.9,
            '•' or '▪' or '–' => 0.5,
            '—' => 1,
            >= 'A' and <= 'Z' => 0.63,
            >= 'a' and <= 'z' => 0.5,
            _ => 0.55,
        };
    }
}
