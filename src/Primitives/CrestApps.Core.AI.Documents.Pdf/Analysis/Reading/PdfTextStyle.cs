using System.Globalization;
using System.Runtime.InteropServices;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// How a run of text is set: the font it uses, its size and its weight.
/// </summary>
/// <param name="FontName">The font name, without the subset prefix an embedded subset carries.</param>
/// <param name="Size">The point size, rounded to a tenth of a point.</param>
/// <param name="Bold">Whether the font is bold.</param>
/// <param name="Italic">Whether the font is italic.</param>
internal readonly record struct PdfTextStyle(string FontName, double Size, bool Bold, bool Italic)
{
    /// <summary>
    /// Gets the style most of the given glyphs are set in.
    /// </summary>
    /// <param name="letters">The glyphs.</param>
    /// <returns>The dominant style, or an empty style when there are no visible glyphs.</returns>
    public static PdfTextStyle Of(IEnumerable<Letter> letters)
    {
        ArgumentNullException.ThrowIfNull(letters);

        var counts = new Dictionary<PdfTextStyle, int>();

        foreach (var letter in letters)
        {
            if (letter is null || string.IsNullOrWhiteSpace(letter.Value))
            {
                continue;
            }

            var style = new PdfTextStyle(
                PdfFonts.CleanName(letter.FontName),
                Math.Round(letter.PointSize, 1),
                PdfFonts.IsBold(letter),
                PdfFonts.IsItalic(letter));

            CollectionsMarshal.GetValueRefOrAddDefault(counts, style, out _)++;
        }

        var best = default(PdfTextStyle);
        var bestCount = 0;

        foreach (var (style, count) in counts)
        {
            if (count > bestCount || (count == bestCount && style.Size > best.Size))
            {
                best = style;
                bestCount = count;
            }
        }

        return best;
    }

    /// <summary>
    /// Describes the style compactly, for example <c>Arial 10.5pt bold</c>.
    /// </summary>
    /// <returns>The description.</returns>
    public string Describe()
    {
        var text = (string.IsNullOrEmpty(FontName) ? "unknown font" : FontName) + " " +
            Size.ToString("0.#", CultureInfo.InvariantCulture) + "pt";

        if (Bold)
        {
            text += " bold";
        }

        if (Italic)
        {
            text += " italic";
        }

        return text;
    }
}
