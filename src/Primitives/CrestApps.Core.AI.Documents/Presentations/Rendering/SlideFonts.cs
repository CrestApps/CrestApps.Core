namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// Chooses what a drawing surface falls back on when a deck's typeface is not installed where the slide is
/// shown.
/// </summary>
/// <remarks>
/// Office fonts such as Calibri and Cambria have metric-compatible open counterparts (Carlito, Caladea) that
/// break lines in the same places, so they come first; after them a font of the same kind, so a serif deck
/// is not shown in a sans-serif.
/// </remarks>
internal static class SlideFonts
{
    private static readonly Dictionary<string, string> _substitutes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Calibri"] = "Carlito",
        ["Calibri Light"] = "Carlito",
        ["Cambria"] = "Caladea",
        ["Arial"] = "Liberation Sans",
        ["Helvetica"] = "Liberation Sans",
        ["Times New Roman"] = "Liberation Serif",
        ["Courier New"] = "Liberation Mono",
        ["Georgia"] = "Gelasio",
        ["Aptos"] = "Segoe UI",
        ["Aptos Display"] = "Segoe UI",
    };

    /// <summary>
    /// Returns a CSS font-family list for a typeface.
    /// </summary>
    /// <param name="font">The typeface.</param>
    /// <returns>The list, with each family quoted.</returns>
    public static string CssFamily(string font)
    {
        var name = string.IsNullOrWhiteSpace(font) ? "Calibri" : font.Trim();
        var families = new List<string> { Quote(name) };

        if (_substitutes.TryGetValue(name, out var substitute))
        {
            families.Add(Quote(substitute));
        }

        families.Add(Generic(name) switch
        {
            "serif" => "Georgia, 'Times New Roman', serif",
            "monospace" => "Consolas, 'Courier New', monospace",
            _ => "'Segoe UI', 'Helvetica Neue', Arial, sans-serif",
        });

        return string.Join(", ", families);
    }

    /// <summary>
    /// Returns the generic kind of a typeface: <c>serif</c>, <c>monospace</c> or <c>sans-serif</c>.
    /// </summary>
    /// <param name="font">The typeface.</param>
    /// <returns>The generic kind.</returns>
    public static string Generic(string font)
    {
        if (string.IsNullOrWhiteSpace(font))
        {
            return "sans-serif";
        }

        if (font.Contains("Mono", StringComparison.OrdinalIgnoreCase) || font.Contains("Consolas", StringComparison.OrdinalIgnoreCase) || font.Contains("Courier", StringComparison.OrdinalIgnoreCase))
        {
            return "monospace";
        }

        string[] serifs = ["Times", "Georgia", "Garamond", "Cambria", "Book", "Palatino", "Constantia", "Caladea", "Serif", "Baskerville", "Didot", "Bodoni", "Rockwell", "Century Schoolbook"];

        return serifs.Any(serif => font.Contains(serif, StringComparison.OrdinalIgnoreCase)) && !font.Contains("Sans", StringComparison.OrdinalIgnoreCase)
            ? "serif"
            : "sans-serif";
    }

    private static string Quote(string name)
    {
        var safe = new string(name.Where(character => character is not ('\'' or '"' or '<' or '>' or '&' or ';' or '\\') && character >= ' ').ToArray());

        return "'" + safe + "'";
    }
}
