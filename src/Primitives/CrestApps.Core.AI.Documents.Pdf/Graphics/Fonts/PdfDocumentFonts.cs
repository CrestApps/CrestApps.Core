using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;

/// <summary>
/// The fonts a document writes text with, embedded when the document is saved.
/// </summary>
/// <remarks>
/// A TrueType font is embedded as a composite (Type 0) font with two-byte glyph codes, so any number of
/// characters can be written with one copy of it. Only the glyphs the document uses are embedded, with
/// their widths and a map back to the text, so the text can still be copied and searched.
/// </remarks>
internal sealed class PdfDocumentFonts
{
    private const int EntriesPerBlock = 100;

    private static readonly ConditionalWeakTable<PdfDocument, PdfDocumentFonts> _documents = [];

    private readonly PdfDocument _document;
    private readonly Dictionary<string, EmbeddedFont> _fonts = new(StringComparer.Ordinal);

    private PdfDocumentFonts(PdfDocument document)
    {
        _document = document;
        document.OnSaving(Finish);
    }

    /// <summary>
    /// Returns the fonts of a document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>Its fonts.</returns>
    public static PdfDocumentFonts For(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return _documents.GetValue(document, created => new PdfDocumentFonts(created));
    }

    /// <summary>
    /// Returns the font dictionary text written with a font refers to, and records the characters written.
    /// </summary>
    /// <param name="font">The font.</param>
    /// <param name="codes">The characters written, as the font encodes them.</param>
    /// <returns>The font dictionary, stored on its own in the document.</returns>
    public PdfDictionary Use(PdfFont font, IEnumerable<PdfFontCode> codes)
    {
        ArgumentNullException.ThrowIfNull(font);

        if (!_fonts.TryGetValue(font.Key, out var embedded))
        {
            embedded = font.Program is null
                ? CreateStandard(font)
                : CreateTrueType(font);

            _fonts[font.Key] = embedded;
        }

        foreach (var code in codes ?? [])
        {
            embedded.Glyphs.TryAdd(code.Code, code.Text);
        }

        return embedded.Dictionary;
    }

    private EmbeddedFont CreateStandard(PdfFont font)
    {
        var dictionary = new PdfDictionary(_document);

        dictionary.Elements.SetName("/Type", "/Font");
        dictionary.Elements.SetName("/Subtype", "/Type1");
        dictionary.Elements.SetName("/BaseFont", font.Key);
        dictionary.Elements.SetName("/Encoding", "/WinAnsiEncoding");
        _document.AddObject(dictionary);

        return new EmbeddedFont(dictionary, null, null, null);
    }

    private EmbeddedFont CreateTrueType(PdfFont font)
    {
        var dictionary = new PdfDictionary(_document);
        var descendant = new PdfDictionary(_document);
        var descriptor = new PdfDictionary(_document);

        dictionary.Elements.SetName("/Type", "/Font");
        dictionary.Elements.SetName("/Subtype", "/Type0");
        dictionary.Elements.SetName("/Encoding", "/Identity-H");
        dictionary.Elements["/DescendantFonts"] = new PdfArray(_document, _document.AddObject(descendant));

        descendant.Elements.SetName("/Type", "/Font");
        descendant.Elements.SetName("/Subtype", "/CIDFontType2");

        var systemInfo = new PdfDictionary(_document);

        systemInfo.Elements.SetString("/Registry", "Adobe");
        systemInfo.Elements.SetString("/Ordering", "Identity");
        systemInfo.Elements.SetInteger("/Supplement", 0);
        descendant.Elements["/CIDSystemInfo"] = systemInfo;
        descendant.Elements.SetName("/CIDToGIDMap", "/Identity");
        descendant.Elements.SetReference("/FontDescriptor", descriptor);
        _document.AddObject(dictionary);

        return new EmbeddedFont(dictionary, descendant, descriptor, font.Program);
    }

    /// <summary>
    /// Writes what each TrueType font needs once every character is known: the subset font file, the
    /// widths, the descriptor and the map back to the text.
    /// </summary>
    private void Finish()
    {
        foreach (var embedded in _fonts.Values)
        {
            if (embedded.Program is null)
            {
                continue;
            }

            var program = embedded.Program;
            var glyphs = embedded.Glyphs.Keys.Order().ToList();
            var name = Tag(program.PostScriptName, glyphs) + "+" + program.PostScriptName;

            embedded.Dictionary.Elements.SetName("/BaseFont", name);
            embedded.Descendant.Elements.SetName("/BaseFont", name);
            embedded.Descendant.Elements["/W"] = Widths(program, glyphs);
            embedded.Descendant.Elements.SetInteger("/DW", (long)Math.Round(program.GetAdvance(0) * 1000));

            WriteDescriptor(embedded.Descriptor, program, name, glyphs);

            var toUnicode = new PdfDictionary(_document);

            toUnicode.CreateStream([]).SetCompressed(Encoding.ASCII.GetBytes(ToUnicode(embedded.Glyphs)));
            embedded.Dictionary.Elements.SetReference("/ToUnicode", toUnicode);
        }
    }

    private void WriteDescriptor(PdfDictionary descriptor, PdfFontProgram program, string name, List<int> glyphs)
    {
        var (left, bottom, right, top) = program.BoundingBox;
        var flags = 32;

        if (program.IsFixedPitch)
        {
            flags |= 1;
        }

        if (program.ItalicAngle != 0)
        {
            flags |= 64;
        }

        descriptor.Elements.SetName("/Type", "/FontDescriptor");
        descriptor.Elements.SetName("/FontName", name);
        descriptor.Elements.SetInteger("/Flags", flags);
        descriptor.Elements["/FontBBox"] = new PdfArray(_document, new[] { left, bottom, right, top }.Select(value => new PdfInteger((long)Math.Round(value * 1000))));
        descriptor.Elements.SetReal("/ItalicAngle", program.ItalicAngle);
        descriptor.Elements.SetInteger("/Ascent", (long)Math.Round(program.Ascent * 1000));
        descriptor.Elements.SetInteger("/Descent", (long)Math.Round(program.Descent * 1000));
        descriptor.Elements.SetInteger("/CapHeight", (long)Math.Round(program.CapHeight * 1000));
        descriptor.Elements.SetInteger("/StemV", 80);

        var subset = PdfTrueTypeSubsetter.Subset(program.Data, glyphs) ?? program.Data;
        var file = new PdfDictionary(_document);

        file.CreateStream([]).SetCompressed(subset);
        file.Elements.SetInteger("/Length1", subset.Length);
        descriptor.Elements.SetReference("/FontFile2", file);
    }

    private PdfArray Widths(PdfFontProgram program, List<int> glyphs)
    {
        var widths = new PdfArray(_document);
        var index = 0;

        // Consecutive glyphs share one entry: the first glyph, then the widths of the run.
        while (index < glyphs.Count)
        {
            var first = glyphs[index];
            var run = new PdfArray(_document);

            while (index < glyphs.Count && glyphs[index] == first + run.Elements.Count)
            {
                run.Elements.Add(new PdfInteger((long)Math.Round(program.GetAdvance(glyphs[index]) * 1000)));
                index++;
            }

            widths.Elements.Add(new PdfInteger(first));
            widths.Elements.Add(run);
        }

        return widths;
    }

    private static string ToUnicode(Dictionary<int, string> glyphs)
    {
        var builder = new StringBuilder()
            .Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n")
            .Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n")
            .Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n")
            .Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");

        var entries = glyphs.Where(entry => !string.IsNullOrEmpty(entry.Value)).OrderBy(entry => entry.Key).ToList();

        for (var start = 0; start < entries.Count; start += EntriesPerBlock)
        {
            var block = entries.Skip(start).Take(EntriesPerBlock).ToList();

            builder.Append(block.Count.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");

            foreach (var (glyph, text) in block)
            {
                builder.Append('<').Append(glyph.ToString("X4", CultureInfo.InvariantCulture)).Append("> <")
                    .Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text))).Append(">\n");
            }

            builder.Append("endbfchar\n");
        }

        return builder.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n").ToString();
    }

    /// <summary>
    /// Builds the six capital letters a subset font's name starts with, the same for the same glyphs.
    /// </summary>
    private static string Tag(string name, List<int> glyphs)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name + ":" + string.Join(',', glyphs)));
        var tag = new char[6];

        for (var index = 0; index < tag.Length; index++)
        {
            tag[index] = (char)('A' + (hash[index] % 26));
        }

        return new string(tag);
    }

    /// <summary>
    /// A font embedded in the document, and the characters written with it.
    /// </summary>
    private sealed class EmbeddedFont
    {
        public EmbeddedFont(PdfDictionary dictionary, PdfDictionary descendant, PdfDictionary descriptor, PdfFontProgram program)
        {
            Dictionary = dictionary;
            Descendant = descendant;
            Descriptor = descriptor;
            Program = program;
        }

        /// <summary>
        /// Gets the font dictionary pages refer to.
        /// </summary>
        public PdfDictionary Dictionary { get; }

        /// <summary>
        /// Gets the CID font of a TrueType font.
        /// </summary>
        public PdfDictionary Descendant { get; }

        /// <summary>
        /// Gets the font descriptor of a TrueType font.
        /// </summary>
        public PdfDictionary Descriptor { get; }

        /// <summary>
        /// Gets the TrueType font, or <see langword="null"/> for a standard one.
        /// </summary>
        public PdfFontProgram Program { get; }

        /// <summary>
        /// Gets the codes written, with the text each stands for.
        /// </summary>
        public Dictionary<int, string> Glyphs { get; } = [];
    }
}
