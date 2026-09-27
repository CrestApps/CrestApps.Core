using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Reads what a glyph's font says about it: the name a reader would recognise, and whether it is bold or
/// italic.
/// </summary>
/// <remarks>
/// A font's flags are unreliable — many generators, MigraDoc among them, leave the bold flag unset on a bold
/// face and say so only in the name (<c>Arial,Bold</c>) — so the name is read as well as the flags.
/// </remarks>
internal static class PdfFonts
{
    private static readonly string[] _boldMarkers = ["bold", "black", "heavy", "semibd", "demibd"];

    private static readonly string[] _italicMarkers = ["italic", "oblique", "slanted"];

    /// <summary>
    /// Removes the six-letter subset prefix an embedded font subset carries, as in <c>ABCDEF+Arial</c>.
    /// </summary>
    /// <param name="name">The font name.</param>
    /// <returns>The name without the prefix, or an empty string.</returns>
    public static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim().TrimStart('/');

        return IsSubset(trimmed)
            ? trimmed[7..]
            : trimmed;
    }

    /// <summary>
    /// Returns whether a font name carries an embedded subset's prefix.
    /// </summary>
    /// <param name="name">The font name.</param>
    /// <returns><see langword="true"/> for a name such as <c>ABCDEF+Arial</c>.</returns>
    public static bool IsSubset(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length < 8 || name[6] != '+')
        {
            return false;
        }

        for (var index = 0; index < 6; index++)
        {
            if (name[index] is < 'A' or > 'Z')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether a glyph is set in a bold face.
    /// </summary>
    /// <param name="letter">The glyph.</param>
    /// <returns><see langword="true"/> when the font's flags, weight or name say it is bold.</returns>
    public static bool IsBold(Letter letter)
    {
        ArgumentNullException.ThrowIfNull(letter);

        var details = letter.FontDetails;

        if (details is not null && (details.IsBold || details.Weight >= 600))
        {
            return true;
        }

        return IsBoldName(letter.FontName ?? details?.Name);
    }

    /// <summary>
    /// Returns whether a glyph is set in an italic face.
    /// </summary>
    /// <param name="letter">The glyph.</param>
    /// <returns><see langword="true"/> when the font's flags or name say it is italic.</returns>
    public static bool IsItalic(Letter letter)
    {
        ArgumentNullException.ThrowIfNull(letter);

        if (letter.FontDetails?.IsItalic == true)
        {
            return true;
        }

        return IsItalicName(letter.FontName ?? letter.FontDetails?.Name);
    }

    /// <summary>
    /// Returns whether a font name names a bold face.
    /// </summary>
    /// <param name="name">The font name.</param>
    /// <returns><see langword="true"/> for names such as <c>Arial,Bold</c> or <c>Inter-SemiBold</c>.</returns>
    public static bool IsBoldName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var marker in _boldMarkers)
        {
            if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether a font name names an italic face.
    /// </summary>
    /// <param name="name">The font name.</param>
    /// <returns><see langword="true"/> for names such as <c>Arial,Italic</c> or <c>MinionPro-It</c>.</returns>
    public static bool IsItalicName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (var marker in _italicMarkers)
        {
            if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Adobe's naming writes italic as a trailing "It": MinionPro-It, MyriadPro-BoldIt.
        return name.EndsWith("It", StringComparison.Ordinal) && name.Contains('-', StringComparison.Ordinal);
    }
}
