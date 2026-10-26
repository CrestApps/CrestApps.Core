using System.Collections.Concurrent;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using UglyToad.PdfPig.Fonts.TrueType.Tables;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;

/// <summary>
/// A TrueType font file read for writing text: how characters map to glyphs, how wide each glyph is, and
/// the metrics a PDF font descriptor names. PdfPig parses the file.
/// </summary>
internal sealed class PdfFontProgram
{
    private readonly TrueTypeFont _font;
    private readonly HorizontalMetricsTable _metrics;
    private readonly ConcurrentDictionary<int, int> _glyphs = new();

    private PdfFontProgram(byte[] data, TrueTypeFont font)
    {
        Data = data;
        _font = font;
        _metrics = font.TableRegister.HorizontalMetricsTable;
        UnitsPerEm = Math.Max(16, (double)font.GetUnitsPerEm());
        GlyphCount = Math.Max(0, (font.TableRegister.IndexToLocationTable?.GlyphOffsets.Count ?? 1) - 1);

        var header = font.TableRegister.HorizontalHeaderTable;
        var os2 = font.TableRegister.Os2Table as Os2RevisedVersion0Table;

        // Windows lays text out with the OS/2 "win" metrics, so lines are spaced the way Word and browsers space them.
        Ascent = os2 is { WindowsAscent: > 0 }
            ? os2.WindowsAscent / UnitsPerEm
            : header.Ascent / UnitsPerEm;
        Descent = os2 is { WindowsDescent: > 0 }
            ? -os2.WindowsDescent / UnitsPerEm
            : header.Descent / UnitsPerEm;
        LineGap = os2 is { WindowsAscent: > 0 }
            ? 0
            : Math.Max(0, (int)header.LineGap) / UnitsPerEm;
        CapHeight = font.TableRegister.Os2Table is Os2Version2To4OpenTypeTable { CapHeight: > 0 } revised
            ? revised.CapHeight / UnitsPerEm
            : Ascent * 0.72;

        var bounds = font.TableRegister.HeaderTable.Bounds;

        BoundingBox = (bounds.Left / UnitsPerEm, bounds.Bottom / UnitsPerEm, bounds.Right / UnitsPerEm, bounds.Top / UnitsPerEm);
        ItalicAngle = font.TableRegister.PostScriptTable?.ItalicAngle ?? 0;
        IsFixedPitch = font.TableRegister.PostScriptTable?.IsFixedPitch is > 0;

        var names = PdfTrueTypeFile.ReadNames(data);

        FamilyName = names.Family ?? font.Name ?? "Font";
        PostScriptName = Sanitize(names.PostScript ?? font.TableRegister.NameTable?.GetPostscriptName() ?? FamilyName);
    }

    /// <summary>
    /// Gets the font file.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>
    /// Gets the family name, as <c>Arial</c>.
    /// </summary>
    public string FamilyName { get; }

    /// <summary>
    /// Gets the PostScript name, as <c>Arial-BoldMT</c>, made safe to write as a PDF name.
    /// </summary>
    public string PostScriptName { get; }

    /// <summary>
    /// Gets the number of glyphs.
    /// </summary>
    public int GlyphCount { get; }

    /// <summary>
    /// Gets the font units in an em.
    /// </summary>
    public double UnitsPerEm { get; }

    /// <summary>
    /// Gets how far the font reaches above the baseline, in ems.
    /// </summary>
    public double Ascent { get; }

    /// <summary>
    /// Gets how far the font reaches below the baseline, in ems, as a negative number.
    /// </summary>
    public double Descent { get; }

    /// <summary>
    /// Gets the extra space between lines the font asks for, in ems.
    /// </summary>
    public double LineGap { get; }

    /// <summary>
    /// Gets the height of capital letters, in ems.
    /// </summary>
    public double CapHeight { get; }

    /// <summary>
    /// Gets the box every glyph fits in, in ems.
    /// </summary>
    public (double Left, double Bottom, double Right, double Top) BoundingBox { get; }

    /// <summary>
    /// Gets the italic angle, in degrees counter-clockwise from upright.
    /// </summary>
    public double ItalicAngle { get; }

    /// <summary>
    /// Gets a value indicating whether every glyph has the same width.
    /// </summary>
    public bool IsFixedPitch { get; }

    /// <summary>
    /// Reads a font file.
    /// </summary>
    /// <param name="data">A TrueType font file, or one face of a collection.</param>
    /// <returns>The font, or <see langword="null"/> when the file is not a TrueType font this writer can embed.</returns>
    public static PdfFontProgram TryLoad(byte[] data)
    {
        if (data is not { Length: > 12 } || PdfTrueTypeFile.IsCollection(data) || PdfTrueTypeFile.IsCff(data))
        {
            return null;
        }

        try
        {
            var font = TrueTypeFontParser.Parse(new TrueTypeDataBytes(data));
            var tables = font.TableRegister;

            if (tables.HorizontalMetricsTable is null ||
                tables.HorizontalHeaderTable is null ||
                tables.HeaderTable is null ||
                tables.IndexToLocationTable is null ||
                font.GetUnitsPerEm() <= 0 ||
                (font.WindowsUnicodeCMap is null && font.WindowsSymbolCMap is null && font.MacRomanCMap is null))
            {
                return null;
            }

            return new PdfFontProgram(data, font);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A font PdfPig cannot read is not used; another one writes the text.
            return null;
        }
    }

    /// <summary>
    /// Returns the glyph that draws a character.
    /// </summary>
    /// <param name="codePoint">The Unicode code point.</param>
    /// <returns>The glyph, or 0 when the font has none for the character.</returns>
    public int GetGlyph(int codePoint)
    {
        if (_glyphs.TryGetValue(codePoint, out var glyph))
        {
            return glyph;
        }

        glyph = 0;

        try
        {
            if (_font.WindowsUnicodeCMap is { } unicode)
            {
                glyph = unicode.CharacterCodeToGlyphIndex(codePoint);
            }

            // A symbol font maps its characters into the private use area at 0xF000.
            if (glyph == 0 && _font.WindowsSymbolCMap is { } symbol && codePoint < 0x100)
            {
                glyph = symbol.CharacterCodeToGlyphIndex(0xF000 + codePoint);
            }

            if (glyph == 0 && _font.WindowsUnicodeCMap is null && _font.MacRomanCMap is { } mac && codePoint < 0x80)
            {
                glyph = mac.CharacterCodeToGlyphIndex(codePoint);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            glyph = 0;
        }

        if (glyph < 0 || glyph >= GlyphCount)
        {
            glyph = 0;
        }

        _glyphs[codePoint] = glyph;

        return glyph;
    }

    /// <summary>
    /// Returns how far a glyph advances the pen.
    /// </summary>
    /// <param name="glyph">The glyph.</param>
    /// <returns>The advance, in ems.</returns>
    public double GetAdvance(int glyph)
    {
        try
        {
            return _metrics.GetAdvanceWidth(glyph) / UnitsPerEm;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return 0.5;
        }
    }

    private static string Sanitize(string name)
    {
        var characters = name.Where(character => character is > ' ' and < '\u007F' and not ('(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%' or '#')).ToArray();

        return characters.Length == 0
            ? "Font"
            : new string(characters);
    }
}
