using System.Text.RegularExpressions;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One font a PDF uses, as its font dictionary describes it.
/// </summary>
internal sealed class PdfFontInfo
{
    /// <summary>
    /// Gets or sets the font dictionary.
    /// </summary>
    public PdfDictionary Dictionary { get; set; }

    /// <summary>
    /// Gets or sets the font's name, with any subset prefix, for example <c>ABCDEF+Arial</c>.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the font type without its slash, for example <c>TrueType</c>, <c>Type0</c> or <c>Type3</c>.
    /// </summary>
    public string Subtype { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the font program is embedded in the file.
    /// </summary>
    public bool IsEmbedded { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether only the glyphs used are embedded (the name carries a
    /// six-letter prefix such as <c>ABCDEF+</c>).
    /// </summary>
    public bool IsSubset { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the font has a <c>/ToUnicode</c> map.
    /// </summary>
    public bool HasToUnicode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the font is one of the 14 standard fonts every viewer carries.
    /// </summary>
    public bool IsStandard14 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text drawn in the font can be turned back into Unicode:
    /// through a <c>/ToUnicode</c> map, a standard encoding of a non-symbolic simple font, or a predefined
    /// CJK character collection.
    /// </summary>
    public bool HasUnicodeMapping { get; set; }

    /// <summary>
    /// Gets or sets the encoding, for example <c>WinAnsiEncoding</c>, <c>Identity-H</c> or <c>custom</c>.
    /// </summary>
    public string Encoding { get; set; }

    /// <summary>
    /// Gets the pages whose resources list the font.
    /// </summary>
    public SortedSet<int> Pages { get; } = [];

    /// <summary>
    /// Describes the font in a few words, for example <c>"ABCDEF+Arial" (TrueType, embedded subset)</c>.
    /// </summary>
    /// <returns>The description.</returns>
    public string Describe()
    {
        var traits = new List<string> { Subtype ?? "unknown type" };

        if (IsEmbedded)
        {
            traits.Add(IsSubset
                ? "embedded subset"
                : "embedded");
        }
        else
        {
            traits.Add(IsStandard14
                ? "standard font, not embedded"
                : "not embedded");
        }

        if (!HasUnicodeMapping)
        {
            traits.Add("no Unicode mapping");
        }

        return $"\"{Name}\" ({string.Join(", ", traits)})";
    }
}

/// <summary>
/// Lists the fonts of a PDF from the resource dictionaries of its pages and of the form XObjects they draw.
/// </summary>
internal static partial class PdfFontInventory
{
    private const int MaxFormDepth = 8;

    private static readonly HashSet<string> _standard14 = new(StringComparer.Ordinal)
    {
        "Times-Roman",
        "Times-Bold",
        "Times-Italic",
        "Times-BoldItalic",
        "Helvetica",
        "Helvetica-Bold",
        "Helvetica-Oblique",
        "Helvetica-BoldOblique",
        "Courier",
        "Courier-Bold",
        "Courier-Oblique",
        "Courier-BoldOblique",
        "Symbol",
        "ZapfDingbats",
    };

    private static readonly HashSet<string> _standardEncodings = new(StringComparer.Ordinal)
    {
        "/WinAnsiEncoding",
        "/MacRomanEncoding",
        "/StandardEncoding",
        "/MacExpertEncoding",
        "/PDFDocEncoding",
    };

    private static readonly HashSet<string> _cjkOrderings = new(StringComparer.Ordinal)
    {
        "GB1",
        "CNS1",
        "Japan1",
        "Korea1",
        "KR",
    };

    /// <summary>
    /// Lists the fonts the given pages use.
    /// </summary>
    /// <param name="document">The document, opened with PDFsharp.</param>
    /// <param name="pages">The one-based pages.</param>
    /// <returns>The fonts, each once, in the order they are first met.</returns>
    public static List<PdfFontInfo> Collect(PdfDocument document, IEnumerable<int> pages)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pages);

        var fonts = new Dictionary<PdfDictionary, PdfFontInfo>(ReferenceEqualityComparer.Instance);
        var order = new List<PdfFontInfo>();

        foreach (var number in pages)
        {
            if (number < 1 || number > document.PageCount)
            {
                continue;
            }

            var resources = PdfObjects.GetInherited(document.Pages[number - 1], "/Resources") as PdfDictionary;

            CollectResources(resources, number, fonts, order, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance), 0);
        }

        return order;
    }

    /// <summary>
    /// Describes one font dictionary.
    /// </summary>
    /// <param name="font">The font dictionary.</param>
    /// <param name="resourceName">The name the font is listed under, used when the font has no name of its own.</param>
    /// <returns>The font.</returns>
    public static PdfFontInfo Describe(PdfDictionary font, string resourceName = null)
    {
        ArgumentNullException.ThrowIfNull(font);

        var subtype = PdfObjects.GetName(font, "/Subtype")?.TrimStart('/');
        var name = PdfObjects.GetName(font, "/BaseFont")?.TrimStart('/') ??
            PdfObjects.GetName(font, "/Name")?.TrimStart('/') ??
            resourceName?.TrimStart('/') ??
            "(unnamed)";

        var descendant = string.Equals(subtype, "Type0", StringComparison.Ordinal)
            ? PdfObjects.Items(PdfObjects.GetArray(font, "/DescendantFonts")).OfType<PdfDictionary>().FirstOrDefault()
            : null;

        var descriptor = PdfObjects.GetDictionary(descendant ?? font, "/FontDescriptor");
        var isType3 = string.Equals(subtype, "Type3", StringComparison.Ordinal);
        var embedded = isType3 ||
            PdfObjects.Get(descriptor, "/FontFile") is PdfDictionary ||
            PdfObjects.Get(descriptor, "/FontFile2") is PdfDictionary ||
            PdfObjects.Get(descriptor, "/FontFile3") is PdfDictionary;

        var baseName = SubsetPrefix().Replace(name, string.Empty);
        var standard = !isType3 && descendant is null && _standard14.Contains(baseName);
        var hasToUnicode = PdfObjects.Get(font, "/ToUnicode") is PdfDictionary;
        var encodingItem = PdfObjects.Get(font, "/Encoding");
        var encodingName = PdfObjects.AsName(encodingItem);
        var flags = (int)(PdfObjects.GetNumber(descriptor, "/Flags") ?? 0);
        var symbolic = (flags & 4) != 0 || baseName is "Symbol" or "ZapfDingbats";

        bool mapped;
        string encoding;

        if (descendant is not null)
        {
            var ordering = PdfObjects.GetText(PdfObjects.GetDictionary(descendant, "/CIDSystemInfo"), "/Ordering");

            encoding = encodingName?.TrimStart('/') ?? "embedded CMap";
            mapped = hasToUnicode || (ordering is not null && _cjkOrderings.Contains(ordering) && !string.Equals(encodingName, "/Identity-H", StringComparison.Ordinal) && !string.Equals(encodingName, "/Identity-V", StringComparison.Ordinal));
        }
        else if (isType3)
        {
            encoding = encodingItem is PdfDictionary
                ? "custom"
                : encodingName?.TrimStart('/') ?? "none";
            mapped = hasToUnicode;
        }
        else
        {
            var baseEncoding = encodingItem is PdfDictionary differences
                ? PdfObjects.GetName(differences, "/BaseEncoding")
                : encodingName;

            if (encodingItem is PdfDictionary)
            {
                encoding = baseEncoding is null
                    ? "custom"
                    : "custom on " + baseEncoding.TrimStart('/');
            }
            else
            {
                encoding = encodingName?.TrimStart('/') ?? "built-in";
            }

            // A simple font maps back to Unicode through a standard encoding, or through the glyph names of
            // a /Differences array; only a symbolic font with nothing but its built-in encoding cannot.
            mapped = hasToUnicode ||
                (!symbolic && (standard || encodingItem is PdfDictionary || (encodingName is not null && _standardEncodings.Contains(encodingName)))) ||
                (standard && symbolic);
        }

        return new PdfFontInfo
        {
            Dictionary = font,
            Name = name,
            Subtype = subtype,
            IsEmbedded = embedded,
            IsSubset = SubsetPrefix().IsMatch(name),
            HasToUnicode = hasToUnicode,
            IsStandard14 = standard,
            HasUnicodeMapping = mapped,
            Encoding = encoding,
        };
    }

    private static void CollectResources(
        PdfDictionary resources,
        int page,
        Dictionary<PdfDictionary, PdfFontInfo> fonts,
        List<PdfFontInfo> order,
        HashSet<PdfDictionary> visited,
        int depth)
    {
        if (resources is null || depth > MaxFormDepth || !visited.Add(resources))
        {
            return;
        }

        var fontDictionary = PdfObjects.GetDictionary(resources, "/Font");

        if (fontDictionary is not null)
        {
            foreach (var pair in fontDictionary.Elements)
            {
                if (PdfObjects.Resolve(pair.Value) is not PdfDictionary font)
                {
                    continue;
                }

                if (!fonts.TryGetValue(font, out var info))
                {
                    info = Describe(font, pair.Key);
                    fonts[font] = info;
                    order.Add(info);
                }

                info.Pages.Add(page);

                // A Type 3 font draws its glyphs with its own resources, which may name further fonts.
                if (string.Equals(info.Subtype, "Type3", StringComparison.Ordinal))
                {
                    CollectResources(PdfObjects.GetDictionary(font, "/Resources"), page, fonts, order, visited, depth + 1);
                }
            }
        }

        var xobjects = PdfObjects.GetDictionary(resources, "/XObject");

        if (xobjects is null)
        {
            return;
        }

        foreach (var pair in xobjects.Elements)
        {
            if (PdfObjects.Resolve(pair.Value) is PdfDictionary form && PdfObjects.IsName(form, "/Subtype", "/Form"))
            {
                CollectResources(PdfObjects.GetDictionary(form, "/Resources"), page, fonts, order, visited, depth + 1);
            }
        }
    }

    [GeneratedRegex("^[A-Z]{6}\\+")]
    private static partial Regex SubsetPrefix();
}
