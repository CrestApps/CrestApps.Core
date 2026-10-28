using System.Text;
using UglyToad.PdfPig.Fonts;
using UglyToad.PdfPig.Fonts.Encodings;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;

/// <summary>
/// A font text is measured and written with: a TrueType font from the host, embedded as the glyphs a
/// document uses, or one of the standard PDF fonts every reader has, for a host without any fonts.
/// </summary>
/// <remarks>
/// A face the font family does not have, such as bold when only regular is installed, is simulated: bold
/// is drawn with an outline, italic slanted.
/// </remarks>
internal sealed class PdfFont
{
    private readonly Dictionary<char, (byte Code, double Width)> _standard;

    private PdfFont(PdfFontProgram program, string family, bool bold, bool italic, bool simulateBold, bool simulateItalic)
    {
        Program = program;
        Family = family;
        IsBold = bold;
        IsItalic = italic;
        SimulatesBold = simulateBold;
        SimulatesItalic = simulateItalic;
        Ascent = program.Ascent;
        Descent = program.Descent;
        LineSpacing = program.Ascent - program.Descent + program.LineGap;
        CapHeight = program.CapHeight;

        // A simulated style is named the way PDF names one, "Arial,Bold", so readers see the text as bold.
        StyleSuffix = (simulateBold, simulateItalic) switch
        {
            (true, true) => ",BoldItalic",
            (true, false) => ",Bold",
            (false, true) => ",Italic",
            _ => string.Empty,
        };

        Key = program.PostScriptName + StyleSuffix;
    }

    private PdfFont(Standard14Font standard, string family, bool bold, bool italic)
    {
        Standard = standard;
        Family = family;
        IsBold = bold;
        IsItalic = italic;

        var metrics = Standard14.GetAdobeFontMetrics(standard);

        Ascent = Math.Max(metrics.Ascender, 700) / 1000;
        Descent = Math.Min(metrics.Descender, -200) / 1000;
        LineSpacing = (Ascent - Descent) * 1.08;
        CapHeight = Math.Max(metrics.CapHeight, 650) / 1000;
        Key = metrics.FontName;
        _standard = [];

        var widths = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var metric in metrics.CharacterMetrics.Values)
        {
            widths.TryAdd(metric.Name, metric.Width.X / 1000);
        }

        // The fonts are written in WinAnsiEncoding, so every code it names a glyph for can be written.
        for (var code = 32; code < 256; code++)
        {
            var name = WinAnsiEncoding.Instance.GetName(code);
            var unicode = GlyphList.AdobeGlyphList.NameToUnicode(name);

            if (unicode is { Length: 1 } && widths.TryGetValue(name, out var width))
            {
                _standard.TryAdd(unicode[0], ((byte)code, width));
            }
        }

        if (_standard.TryGetValue(' ', out var space))
        {
            _standard.TryAdd(' ', space);
        }
    }

    /// <summary>
    /// Gets the TrueType font, or <see langword="null"/> for a standard font.
    /// </summary>
    public PdfFontProgram Program { get; }

    /// <summary>
    /// Gets the standard font, when the font is one.
    /// </summary>
    public Standard14Font? Standard { get; }

    /// <summary>
    /// Gets the family the font was asked for by.
    /// </summary>
    public string Family { get; }

    /// <summary>
    /// Gets a value indicating whether the font writes bold, drawn or simulated.
    /// </summary>
    public bool IsBold { get; }

    /// <summary>
    /// Gets a value indicating whether the font writes italic, drawn or simulated.
    /// </summary>
    public bool IsItalic { get; }

    /// <summary>
    /// Gets a value indicating whether bold is simulated, because the family has no bold face.
    /// </summary>
    public bool SimulatesBold { get; }

    /// <summary>
    /// Gets a value indicating whether italic is simulated, because the family has no italic face.
    /// </summary>
    public bool SimulatesItalic { get; }

    /// <summary>
    /// Gets how far the font reaches above the baseline, in ems.
    /// </summary>
    public double Ascent { get; }

    /// <summary>
    /// Gets how far the font reaches below the baseline, in ems, as a negative number.
    /// </summary>
    public double Descent { get; }

    /// <summary>
    /// Gets the distance between the baselines of single-spaced lines, in ems.
    /// </summary>
    public double LineSpacing { get; }

    /// <summary>
    /// Gets the height of capital letters, in ems.
    /// </summary>
    public double CapHeight { get; }

    /// <summary>
    /// Gets a value indicating whether the font program is embedded in the documents it writes.
    /// </summary>
    public bool IsEmbedded => Program is not null;

    /// <summary>
    /// Gets what identifies the embedded font, so fonts drawn from the same file in the same way share one copy.
    /// </summary>
    internal string Key { get; }

    /// <summary>
    /// Gets what the embedded font's name ends with when a style is simulated, such as <c>,Bold</c>.
    /// </summary>
    internal string StyleSuffix { get; } = string.Empty;

    /// <summary>
    /// Creates a font from a TrueType font file.
    /// </summary>
    /// <param name="program">The font file.</param>
    /// <param name="family">The family it was asked for by.</param>
    /// <param name="bold">Whether it writes bold.</param>
    /// <param name="italic">Whether it writes italic.</param>
    /// <param name="simulateBold">Whether bold is simulated.</param>
    /// <param name="simulateItalic">Whether italic is simulated.</param>
    /// <returns>The font.</returns>
    public static PdfFont FromProgram(PdfFontProgram program, string family, bool bold, bool italic, bool simulateBold, bool simulateItalic)
    {
        ArgumentNullException.ThrowIfNull(program);

        return new PdfFont(program, family, bold, italic, simulateBold, simulateItalic);
    }

    /// <summary>
    /// Creates one of the standard fonts: Helvetica, Times or Courier in the face asked for.
    /// </summary>
    /// <param name="family">The family it was asked for by.</param>
    /// <param name="bold">Whether it writes bold.</param>
    /// <param name="italic">Whether it writes italic.</param>
    /// <returns>The font.</returns>
    public static PdfFont FromStandard(string family, bool bold, bool italic)
    {
        var normalized = (family ?? string.Empty).ToLowerInvariant();
        var isMono = normalized.Contains("courier", StringComparison.Ordinal) || normalized.Contains("mono", StringComparison.Ordinal) || normalized.Contains("consol", StringComparison.Ordinal);
        var isSerif = !isMono && (normalized.Contains("times", StringComparison.Ordinal) || normalized.Contains("georgia", StringComparison.Ordinal) || (normalized.Contains("serif", StringComparison.Ordinal) && !normalized.Contains("sans", StringComparison.Ordinal)));

        var standard = (isMono, isSerif, bold, italic) switch
        {
            (true, _, true, true) => Standard14Font.CourierBoldOblique,
            (true, _, true, false) => Standard14Font.CourierBold,
            (true, _, false, true) => Standard14Font.CourierOblique,
            (true, _, false, false) => Standard14Font.Courier,
            (false, true, true, true) => Standard14Font.TimesBoldItalic,
            (false, true, true, false) => Standard14Font.TimesBold,
            (false, true, false, true) => Standard14Font.TimesItalic,
            (false, true, false, false) => Standard14Font.TimesRoman,
            (false, false, true, true) => Standard14Font.HelveticaBoldOblique,
            (false, false, true, false) => Standard14Font.HelveticaBold,
            (false, false, false, true) => Standard14Font.HelveticaOblique,
            _ => Standard14Font.Helvetica,
        };

        return new PdfFont(standard, family, bold, italic);
    }

    /// <summary>
    /// Returns whether the font draws a character with a glyph of its own.
    /// </summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns><see langword="true"/> when the character has a glyph.</returns>
    public bool HasGlyph(int codePoint)
    {
        return Program is not null
            ? Program.GetGlyph(codePoint) != 0
            : codePoint <= char.MaxValue && _standard.ContainsKey((char)codePoint);
    }

    /// <summary>
    /// Measures text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The font size, in points.</param>
    /// <returns>The width, in points.</returns>
    public double Measure(string text, double size)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var width = 0d;

        foreach (var code in Encode(text))
        {
            width += code.Advance;
        }

        return width * size;
    }

    /// <summary>
    /// Turns text into the codes the font writes it with.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>For each character: its code (a glyph for a TrueType font, a byte for a standard one), its advance in ems, and the text it stands for.</returns>
    public List<PdfFontCode> Encode(string text)
    {
        var codes = new List<PdfFontCode>(text?.Length ?? 0);

        if (string.IsNullOrEmpty(text))
        {
            return codes;
        }

        for (var index = 0; index < text.Length; index++)
        {
            var length = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
                ? 2
                : 1;

            var codePoint = length == 2
                ? char.ConvertToUtf32(text[index], text[index + 1])
                : char.IsSurrogate(text[index])
                    ? 0xFFFD
                    : text[index];

            var source = text.Substring(index, length);

            index += length - 1;
            codes.Add(Program is not null
                ? EncodeTrueType(codePoint, source)
                : EncodeStandard(codePoint, source));
        }

        return codes;
    }

    private PdfFontCode EncodeTrueType(int codePoint, string source)
    {
        var glyph = Program.GetGlyph(codePoint);

        // A space the font has no glyph for, such as a no-break space, draws as its space.
        if (glyph == 0 && (codePoint == '\t' || (codePoint <= char.MaxValue && char.IsWhiteSpace((char)codePoint))))
        {
            glyph = Program.GetGlyph(' ');
        }

        return new PdfFontCode(glyph, Program.GetAdvance(glyph), source);
    }

    private PdfFontCode EncodeStandard(int codePoint, string source)
    {
        var character = codePoint <= char.MaxValue
            ? (char)codePoint
            : '?';

        if (!_standard.TryGetValue(character, out var entry) && !_standard.TryGetValue(Fallback(character), out entry))
        {
            entry = _standard['?'];
        }

        return new PdfFontCode(entry.Code, entry.Width, source);
    }

    private char Fallback(char character)
    {
        if (char.IsWhiteSpace(character))
        {
            return ' ';
        }

        // An accented letter a standard font lacks is written without its accent.
        var decomposed = character.ToString().Normalize(NormalizationForm.FormD);

        return decomposed.Length > 1 && _standard.ContainsKey(decomposed[0])
            ? decomposed[0]
            : '?';
    }
}
