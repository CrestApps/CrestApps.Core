using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// What a PDF's XMP metadata stream says: the standards it claims and the properties the document
/// information dictionary mirrors.
/// </summary>
internal sealed class PdfXmpInfo
{
    /// <summary>
    /// The namespace of PDF/A identification.
    /// </summary>
    public static readonly XNamespace PdfAId = "http://www.aiim.org/pdfa/ns/id/";

    /// <summary>
    /// The namespace of PDF/UA identification.
    /// </summary>
    public static readonly XNamespace PdfUAId = "http://www.aiim.org/pdfua/ns/id/";

    /// <summary>
    /// The RDF namespace.
    /// </summary>
    public static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>
    /// The Dublin Core namespace.
    /// </summary>
    public static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";

    /// <summary>
    /// The Adobe PDF namespace.
    /// </summary>
    public static readonly XNamespace AdobePdf = "http://ns.adobe.com/pdf/1.3/";

    /// <summary>
    /// The XMP basic namespace.
    /// </summary>
    public static readonly XNamespace XmpBasic = "http://ns.adobe.com/xap/1.0/";

    /// <summary>
    /// The XML namespace, which <c>xml:lang</c> belongs to.
    /// </summary>
    public static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>
    /// Gets a value indicating whether the document has a metadata stream.
    /// </summary>
    public bool IsPresent { get; private set; }

    /// <summary>
    /// Gets why the stream could not be read as XML, or <see langword="null"/>.
    /// </summary>
    public string ParseError { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the metadata stream is compressed or otherwise filtered.
    /// </summary>
    public bool IsFiltered { get; private set; }

    /// <summary>
    /// Gets the parsed metadata, or <see langword="null"/>.
    /// </summary>
    public XDocument Document { get; private set; }

    /// <summary>
    /// Gets the PDF/A part the file claims, or <see langword="null"/>.
    /// </summary>
    public int? PdfAPart { get; private set; }

    /// <summary>
    /// Gets the PDF/A conformance level the file claims, for example <c>B</c>, or <see langword="null"/>.
    /// </summary>
    public string PdfAConformance { get; private set; }

    /// <summary>
    /// Gets the PDF/UA part the file claims, or <see langword="null"/>.
    /// </summary>
    public int? PdfUAPart { get; private set; }

    /// <summary>
    /// Gets the title (<c>dc:title</c>).
    /// </summary>
    public string Title { get; private set; }

    /// <summary>
    /// Gets the authors (<c>dc:creator</c>), joined.
    /// </summary>
    public string Authors { get; private set; }

    /// <summary>
    /// Gets the subject (<c>dc:description</c>).
    /// </summary>
    public string Subject { get; private set; }

    /// <summary>
    /// Gets the keywords (<c>pdf:Keywords</c>).
    /// </summary>
    public string Keywords { get; private set; }

    /// <summary>
    /// Gets the producer (<c>pdf:Producer</c>).
    /// </summary>
    public string Producer { get; private set; }

    /// <summary>
    /// Gets the creating application (<c>xmp:CreatorTool</c>).
    /// </summary>
    public string CreatorTool { get; private set; }

    /// <summary>
    /// Reads the metadata stream of a document.
    /// </summary>
    /// <param name="document">The document, opened with PDFsharp.</param>
    /// <returns>What the metadata says.</returns>
    public static PdfXmpInfo Read(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var stream = PdfObjectReader.GetDictionary(document.Internals.Catalog, "/Metadata");

        if (stream?.Stream is null)
        {
            return new PdfXmpInfo();
        }

        byte[] bytes;

        try
        {
            bytes = stream.Stream.UnfilteredValue;
        }
        catch (Exception ex)
        {
            return new PdfXmpInfo
            {
                IsPresent = true,
                ParseError = "the stream cannot be decoded: " + ex.Message,
            };
        }

        var info = Parse(bytes);

        info.IsFiltered = PdfObjectReader.Get(stream, "/Filter") is not null;

        return info;
    }

    /// <summary>
    /// Reads an XMP packet.
    /// </summary>
    /// <param name="bytes">The packet.</param>
    /// <returns>What it says.</returns>
    public static PdfXmpInfo Parse(byte[] bytes)
    {
        var info = new PdfXmpInfo { IsPresent = bytes is not null };

        if (bytes is null || bytes.Length == 0)
        {
            info.ParseError = bytes is null
                ? null
                : "the stream is empty";

            return info;
        }

        try
        {
            info.Document = Load(bytes);
        }
        catch (XmlException ex)
        {
            info.ParseError = ex.Message;

            return info;
        }

        info.PdfAPart = ReadInteger(info.Document, PdfAId + "part");
        info.PdfAConformance = ReadProperty(info.Document, PdfAId + "conformance")?.Trim().ToUpperInvariant();
        info.PdfUAPart = ReadInteger(info.Document, PdfUAId + "part");
        info.Title = ReadProperty(info.Document, DublinCore + "title");
        info.Authors = ReadProperty(info.Document, DublinCore + "creator");
        info.Subject = ReadProperty(info.Document, DublinCore + "description");
        info.Keywords = ReadProperty(info.Document, AdobePdf + "Keywords");
        info.Producer = ReadProperty(info.Document, AdobePdf + "Producer");
        info.CreatorTool = ReadProperty(info.Document, XmpBasic + "CreatorTool");

        return info;
    }

    /// <summary>
    /// Parses an XMP packet as XML, refusing document type definitions.
    /// </summary>
    /// <param name="bytes">The packet.</param>
    /// <returns>The document.</returns>
    public static XDocument Load(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false,
        };

        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, settings);

        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    /// <summary>
    /// Reads a simple or array-valued property, written either as an element or as an attribute of an
    /// <c>rdf:Description</c>.
    /// </summary>
    /// <param name="document">The metadata.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The value — the default-language entry of a language alternative, the entries of a list joined — or <see langword="null"/>.</returns>
    public static string ReadProperty(XDocument document, XName name)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var description in document.Descendants(Rdf + "Description"))
        {
            var attribute = description.Attribute(name);

            if (attribute is not null)
            {
                return attribute.Value;
            }
        }

        var element = document.Descendants(name).FirstOrDefault();

        if (element is null)
        {
            return null;
        }

        var items = element.Descendants(Rdf + "li").ToList();

        if (items.Count == 0)
        {
            return element.Value;
        }

        var preferred = items.FirstOrDefault(item => string.Equals((string)item.Attribute(Xml + "lang"), "x-default", StringComparison.OrdinalIgnoreCase));

        if (preferred is not null || element.Element(Rdf + "Alt") is not null)
        {
            return (preferred ?? items[0]).Value;
        }

        return string.Join(", ", items.Select(item => item.Value).Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static int? ReadInteger(XDocument document, XName name)
    {
        var text = ReadProperty(document, name);

        return int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
