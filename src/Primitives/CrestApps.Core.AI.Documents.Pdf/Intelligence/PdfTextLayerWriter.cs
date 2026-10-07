using System.Globalization;
using System.Text;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Intelligence;

/// <summary>
/// Adds recognised text to a page as an invisible layer, so a scanned page can be searched, selected and
/// read aloud while it still looks exactly as it did.
/// </summary>
/// <remarks>
/// The text is drawn in text rendering mode 3 (neither filled nor stroked) with the standard Helvetica font,
/// which every PDF reader has, so nothing needs embedding. A transcription carries no positions, so its
/// lines are spread from the top of the page to the bottom and squeezed to the page's width: a search finds
/// the words and a reader can copy them, but a highlight lands only roughly where the words are printed.
/// </remarks>
internal static class PdfTextLayerWriter
{
    private const double Margin = 36;
    private const double MaxFontSize = 11;
    private const double AverageGlyphWidth = 0.5;

    private static readonly Dictionary<char, byte> _winAnsiExtras = new()
    {
        ['€'] = 0x80,
        ['‚'] = 0x82,
        ['ƒ'] = 0x83,
        ['„'] = 0x84,
        ['…'] = 0x85,
        ['†'] = 0x86,
        ['‡'] = 0x87,
        ['ˆ'] = 0x88,
        ['‰'] = 0x89,
        ['Š'] = 0x8A,
        ['‹'] = 0x8B,
        ['Œ'] = 0x8C,
        ['Ž'] = 0x8E,
        ['‘'] = 0x91,
        ['’'] = 0x92,
        ['“'] = 0x93,
        ['”'] = 0x94,
        ['•'] = 0x95,
        ['–'] = 0x96,
        ['—'] = 0x97,
        ['˜'] = 0x98,
        ['™'] = 0x99,
        ['š'] = 0x9A,
        ['›'] = 0x9B,
        ['œ'] = 0x9C,
        ['ž'] = 0x9E,
        ['Ÿ'] = 0x9F,
    };

    /// <summary>
    /// Adds text to a page as an invisible layer.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="text">The text, one line per line; Markdown table and heading marks are dropped.</param>
    /// <returns><see langword="true"/> when a layer was added; <see langword="false"/> when the text had no lines.</returns>
    public static bool AddInvisibleText(PdfPage page, string text)
    {
        ArgumentNullException.ThrowIfNull(page);

        var lines = CleanLines(text);

        if (lines.Count == 0)
        {
            return false;
        }

        var fontName = AddFontResource(page);
        var area = page.HasCropBox ? page.CropBox : page.MediaBox;
        var left = Math.Min(area.X1, area.X2) + Margin;
        var right = Math.Max(area.X1, area.X2) - Margin;
        var top = Math.Max(area.Y1, area.Y2) - Margin;
        var bottom = Math.Min(area.Y1, area.Y2) + Margin;
        var width = Math.Max(right - left, 10);
        var height = Math.Max(top - bottom, 10);
        var lineHeight = height / lines.Count;
        var fontSize = Math.Clamp(lineHeight * 0.8, 1, MaxFontSize);
        var content = new StringBuilder();

        content.Append("q\nBT\n3 Tr\n");
        content.Append(fontName).Append(' ').Append(Number(fontSize)).Append(" Tf\n");

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var estimated = line.Length * AverageGlyphWidth * fontSize;
            var scale = estimated <= width
                ? 100
                : Math.Max(1, width / estimated * 100);
            var baseline = top - (index * lineHeight) - fontSize;

            content.Append(Number(scale)).Append(" Tz\n");
            content.Append("1 0 0 1 ").Append(Number(left)).Append(' ').Append(Number(baseline)).Append(" Tm\n");
            content.Append('(').Append(Encode(line)).Append(") Tj\n");
        }

        content.Append("ET\nQ\n");

        // The page's own drawing is closed in its own graphics state first, so whatever transform or colour
        // it leaves behind cannot move the new text.
        page.Contents.PrependContent().CreateStream(Encoding.ASCII.GetBytes("q\n"));
        page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("Q\n" + content));

        return true;
    }

    /// <summary>
    /// Turns a transcription into the plain lines the layer draws.
    /// </summary>
    /// <param name="text">The transcription.</param>
    /// <returns>The lines, without Markdown table rules, cell bars, heading marks or emphasis.</returns>
    public static List<string> CleanLines(string text)
    {
        var lines = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return lines;
        }

        foreach (var raw in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();

            // A Markdown table's rule row (|---|:---:|) carries no text.
            if (line.Length == 0 || line.All(character => character is '|' or '-' or ':' or ' ' or '='))
            {
                continue;
            }

            line = line.TrimStart('#', '>', ' ')
                .Replace("**", string.Empty, StringComparison.Ordinal)
                .Replace("__", string.Empty, StringComparison.Ordinal)
                .Replace('|', ' ');

            line = PdfCorpus.CollapseWhitespace(line);

            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    /// <summary>
    /// Writes text as the body of a PDF literal string in WinAnsi encoding.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped string body; characters WinAnsi cannot show are written without their accents, or as <c>?</c>.</returns>
    public static string Encode(string text)
    {
        var builder = new StringBuilder(text.Length + 8);

        foreach (var character in text ?? string.Empty)
        {
            var code = ToWinAnsi(character);

            switch (code)
            {
                case (byte)'(':
                case (byte)')':
                case (byte)'\\':
                    builder.Append('\\').Append((char)code);

                    break;

                case >= 0x20 and < 0x7F:
                    builder.Append((char)code);

                    break;

                default:
                    builder.Append('\\').Append(Convert.ToString(code, 8).PadLeft(3, '0'));

                    break;
            }
        }

        return builder.ToString();
    }

    private static byte ToWinAnsi(char character)
    {
        if (character is >= ' ' and <= '~' or >= ' ' and <= 'ÿ')
        {
            return (byte)character;
        }

        if (_winAnsiExtras.TryGetValue(character, out var code))
        {
            return code;
        }

        if (char.IsWhiteSpace(character))
        {
            return (byte)' ';
        }

        // An accented letter WinAnsi lacks is still found by a search for its base letter.
        var decomposed = character.ToString().Normalize(NormalizationForm.FormD);

        return decomposed.Length > 0 && decomposed[0] is >= ' ' and <= '~'
            ? (byte)decomposed[0]
            : (byte)'?';
    }

    private static string AddFontResource(PdfPage page)
    {
        var document = page.Owner;
        var resources = page.Resources;
        var fonts = resources.Elements.GetDictionary("/Font");

        if (fonts is null)
        {
            fonts = new PdfDictionary(document);
            resources.Elements["/Font"] = fonts;
        }

        var name = "/FOcrText";

        for (var number = 2; fonts.Elements.ContainsKey(name); number++)
        {
            name = "/FOcrText" + number.ToString(CultureInfo.InvariantCulture);
        }

        var font = new PdfDictionary(document);

        font.Elements["/Type"] = new PdfName("/Font");
        font.Elements["/Subtype"] = new PdfName("/Type1");
        font.Elements["/BaseFont"] = new PdfName("/Helvetica");
        font.Elements["/Encoding"] = new PdfName("/WinAnsiEncoding");
        document.Internals.AddObject(font);

        fonts.Elements[name] = font.Reference;

        return name;
    }

    private static string Number(double value)
    {
        return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
