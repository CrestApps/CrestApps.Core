using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Reads and writes the XMP metadata packet a PDF carries next to its document information dictionary.
/// </summary>
/// <remarks>
/// Viewers that read XMP show it in preference to the information dictionary, so the two have to agree.
/// A packet is rewritten by taking the one the file had, removing the properties that mirror the
/// information dictionary, and adding them again from the final values — every other property, such as a
/// PDF/A or PDF/UA identification or an extension schema, is kept as it was.
/// </remarks>
internal static class PdfXmpPacket
{
    /// <summary>
    /// The RDF namespace.
    /// </summary>
    public static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    /// <summary>
    /// The Dublin Core namespace.
    /// </summary>
    public static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";

    /// <summary>
    /// The Adobe PDF schema namespace.
    /// </summary>
    public static readonly XNamespace AdobePdf = "http://ns.adobe.com/pdf/1.3/";

    /// <summary>
    /// The XMP basic schema namespace.
    /// </summary>
    public static readonly XNamespace XmpBasic = "http://ns.adobe.com/xap/1.0/";

    /// <summary>
    /// The XMP media management schema namespace.
    /// </summary>
    public static readonly XNamespace XmpMediaManagement = "http://ns.adobe.com/xap/1.0/mm/";

    /// <summary>
    /// The PDF/A identification schema namespace.
    /// </summary>
    public static readonly XNamespace PdfAId = "http://www.aiim.org/pdfa/ns/id/";

    /// <summary>
    /// The PDF/UA identification schema namespace.
    /// </summary>
    public static readonly XNamespace PdfUaId = "http://www.aiim.org/pdfua/ns/id/";

    private static readonly XNamespace _xmpMeta = "adobe:ns:meta/";

    private static readonly (XNamespace Namespace, string Name)[] _managed =
    [
        (DublinCore, "title"),
        (DublinCore, "creator"),
        (DublinCore, "description"),
        (DublinCore, "language"),
        (DublinCore, "format"),
        (AdobePdf, "Keywords"),
        (AdobePdf, "Producer"),
        (XmpBasic, "CreatorTool"),
        (XmpBasic, "CreateDate"),
        (XmpBasic, "ModifyDate"),
        (XmpBasic, "MetadataDate"),
        (XmpMediaManagement, "InstanceID"),
    ];

    /// <summary>
    /// Reads a packet.
    /// </summary>
    /// <param name="packet">The bytes of the metadata stream.</param>
    /// <returns>The packet's <c>rdf:RDF</c> element, or <see langword="null"/> when it is missing or not well-formed.</returns>
    public static XElement Parse(byte[] packet)
    {
        if (packet is null || packet.Length == 0)
        {
            return null;
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
        };

        try
        {
            using var stream = new MemoryStream(packet, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            var document = XDocument.Load(reader);

            return document.Descendants(Rdf + "RDF").FirstOrDefault();
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Describes the conformance a packet claims, such as <c>PDF/A-2B</c> or <c>PDF/UA-1</c>.
    /// </summary>
    /// <param name="rdf">The packet's <c>rdf:RDF</c> element.</param>
    /// <returns>The claims, or an empty list.</returns>
    public static List<string> DescribeConformance(XElement rdf)
    {
        var claims = new List<string>();

        if (rdf is null)
        {
            return claims;
        }

        var part = ReadProperty(rdf, PdfAId, "part");

        if (!string.IsNullOrWhiteSpace(part))
        {
            claims.Add("PDF/A-" + part.Trim() + (ReadProperty(rdf, PdfAId, "conformance")?.Trim().ToUpperInvariant() ?? string.Empty));
        }

        var uaPart = ReadProperty(rdf, PdfUaId, "part");

        if (!string.IsNullOrWhiteSpace(uaPart))
        {
            claims.Add("PDF/UA-" + uaPart.Trim());
        }

        return claims;
    }

    /// <summary>
    /// Reads the first value of a property, written either as an element or as an attribute of an
    /// <c>rdf:Description</c>, taking the first item of an <c>rdf:Alt</c>, <c>rdf:Seq</c> or <c>rdf:Bag</c>.
    /// </summary>
    /// <param name="rdf">The packet's <c>rdf:RDF</c> element.</param>
    /// <param name="ns">The property's namespace.</param>
    /// <param name="name">The property's local name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static string ReadProperty(XElement rdf, XNamespace ns, string name)
    {
        if (rdf is null)
        {
            return null;
        }

        foreach (var description in rdf.Elements(Rdf + "Description"))
        {
            var attribute = description.Attribute(ns + name);

            if (attribute is not null)
            {
                return attribute.Value;
            }

            var element = description.Element(ns + name);

            if (element is null)
            {
                continue;
            }

            var container = element.Elements().FirstOrDefault(child => child.Name == Rdf + "Alt" || child.Name == Rdf + "Seq" || child.Name == Rdf + "Bag");

            return container is null
                ? element.Value
                : container.Elements(Rdf + "li").FirstOrDefault()?.Value;
        }

        return null;
    }

    /// <summary>
    /// Builds a packet from the one a file had and the final values of its document information.
    /// </summary>
    /// <param name="original">The original packet's <c>rdf:RDF</c> element, or <see langword="null"/>.</param>
    /// <param name="values">The values the packet mirrors.</param>
    /// <param name="keepOtherProperties">Whether the original's other properties are kept; when <see langword="false"/> only its PDF/A and PDF/UA identification is.</param>
    /// <param name="dropConformance">Whether the original's PDF/A identification is dropped, because the edit broke it.</param>
    /// <returns>The packet as UTF-8 bytes.</returns>
    public static byte[] Build(XElement original, PdfXmpValues values, bool keepOtherProperties, bool dropConformance)
    {
        ArgumentNullException.ThrowIfNull(values);

        var rdf = original is null
            ? new XElement(Rdf + "RDF")
            : new XElement(original);

        rdf.SetAttributeValue(XNamespace.Xmlns + "rdf", Rdf.NamespaceName);

        foreach (var description in rdf.Elements(Rdf + "Description").ToList())
        {
            Strip(description, keepOtherProperties, dropConformance);
        }

        // Anything that is not a description (stray text, other elements) has no meaning to a reader.
        foreach (var other in rdf.Elements().Where(element => element.Name != Rdf + "Description").ToList())
        {
            other.Remove();
        }

        rdf.Add(CreateDescription(values, keepsDocumentId: rdf.Descendants(XmpMediaManagement + "DocumentID").Any() || rdf.Elements(Rdf + "Description").Any(description => description.Attribute(XmpMediaManagement + "DocumentID") is not null)));

        var root = new XElement(
            _xmpMeta + "xmpmeta",
            new XAttribute(XNamespace.Xmlns + "x", _xmpMeta.NamespaceName),
            rdf);

        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = " ",
            Encoding = new UTF8Encoding(false),
            NewLineChars = "\n",
        };

        var builder = new StringBuilder();

        using (var writer = XmlWriter.Create(builder, settings))
        {
            root.WriteTo(writer);
        }

        var packet = "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n" + builder + "\n<?xpacket end=\"w\"?>";

        return new UTF8Encoding(false).GetBytes(packet);
    }

    /// <summary>
    /// Converts a PDF date (<c>D:YYYYMMDDHHmmSSOHH'mm'</c>) to the form XMP uses.
    /// </summary>
    /// <param name="pdfDate">The PDF date.</param>
    /// <returns>For example <c>2026-09-27T10:32:48-07:00</c>, or <see langword="null"/> when the date cannot be read.</returns>
    public static string ToXmpDate(string pdfDate)
    {
        if (string.IsNullOrWhiteSpace(pdfDate))
        {
            return null;
        }

        var text = pdfDate.Trim();

        if (text.StartsWith("D:", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        var digits = 0;

        while (digits < text.Length && digits < 14 && char.IsAsciiDigit(text[digits]))
        {
            digits++;
        }

        if (digits < 4)
        {
            return null;
        }

        string Part(int start, int length, string fallback)
        {
            return digits >= start + length ? text.Substring(start, length) : fallback;
        }

        var year = text[..4];
        var month = Part(4, 2, "01");
        var day = Part(6, 2, "01");
        var hour = Part(8, 2, "00");
        var minute = Part(10, 2, "00");
        var second = Part(12, 2, "00");
        var rest = text[digits..].Replace("'", string.Empty, StringComparison.Ordinal);

        if (!int.TryParse(month, NumberStyles.None, CultureInfo.InvariantCulture, out var monthNumber) || monthNumber is < 1 or > 12 ||
            !int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var dayNumber) || dayNumber is < 1 or > 31)
        {
            return null;
        }

        var offset = "Z";

        if (rest.Length >= 3 && (rest[0] == '+' || rest[0] == '-'))
        {
            var offsetHours = rest.Substring(1, 2);
            var offsetMinutes = rest.Length >= 5 ? rest.Substring(3, 2) : "00";

            offset = rest[0] + offsetHours + ":" + offsetMinutes;
        }

        return $"{year}-{month}-{day}T{hour}:{minute}:{second}{offset}";
    }

    private static void Strip(XElement description, bool keepOtherProperties, bool dropConformance)
    {
        bool IsConformance(XName name)
        {
            return name.Namespace == PdfAId || name.Namespace == PdfUaId;
        }

        bool Remove(XName name)
        {
            if (name.Namespace == PdfAId)
            {
                return dropConformance;
            }

            if (name.Namespace == PdfUaId)
            {
                return false;
            }

            return !keepOtherProperties || _managed.Any(entry => entry.Namespace == name.Namespace && entry.Name == name.LocalName);
        }

        foreach (var element in description.Elements().Where(element => Remove(element.Name)).ToList())
        {
            element.Remove();
        }

        foreach (var attribute in description.Attributes()
            .Where(attribute => !attribute.IsNamespaceDeclaration && attribute.Name != Rdf + "about" && Remove(attribute.Name))
            .ToList())
        {
            attribute.Remove();
        }

        var hasProperties = description.HasElements ||
            description.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration && attribute.Name != Rdf + "about");

        if (!hasProperties)
        {
            description.Remove();

            return;
        }

        if (!keepOtherProperties && !description.Elements().Any(element => !IsConformance(element.Name)))
        {
            // Only the identification is left; the namespace declarations of removed schemas go too.
            foreach (var declaration in description.Attributes().Where(attribute => attribute.IsNamespaceDeclaration && attribute.Value != PdfAId.NamespaceName && attribute.Value != PdfUaId.NamespaceName).ToList())
            {
                declaration.Remove();
            }
        }
    }

    private static XElement CreateDescription(PdfXmpValues values, bool keepsDocumentId)
    {
        var description = new XElement(
            Rdf + "Description",
            new XAttribute(Rdf + "about", string.Empty),
            new XAttribute(XNamespace.Xmlns + "dc", DublinCore.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "pdf", AdobePdf.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xmp", XmpBasic.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xmpMM", XmpMediaManagement.NamespaceName));

        description.Add(new XElement(DublinCore + "format", "application/pdf"));

        var title = Clean(values.Title);
        var author = Clean(values.Author);
        var subject = Clean(values.Subject);
        var language = Clean(values.Language);
        var keywords = Clean(values.Keywords);
        var producer = Clean(values.Producer);
        var creator = Clean(values.Creator);

        if (title is not null)
        {
            description.Add(new XElement(DublinCore + "title", Alternative(title)));
        }

        if (author is not null)
        {
            description.Add(new XElement(DublinCore + "creator", new XElement(Rdf + "Seq", new XElement(Rdf + "li", author))));
        }

        if (subject is not null)
        {
            description.Add(new XElement(DublinCore + "description", Alternative(subject)));
        }

        if (language is not null)
        {
            description.Add(new XElement(DublinCore + "language", new XElement(Rdf + "Bag", new XElement(Rdf + "li", language))));
        }

        if (keywords is not null)
        {
            description.Add(new XElement(AdobePdf + "Keywords", keywords));
        }

        if (producer is not null)
        {
            description.Add(new XElement(AdobePdf + "Producer", producer));
        }

        if (creator is not null)
        {
            description.Add(new XElement(XmpBasic + "CreatorTool", creator));
        }

        var created = ToXmpDate(values.CreationDate);
        var modified = ToXmpDate(values.ModificationDate);

        if (created is not null)
        {
            description.Add(new XElement(XmpBasic + "CreateDate", created));
        }

        if (modified is not null)
        {
            description.Add(new XElement(XmpBasic + "ModifyDate", modified));
            description.Add(new XElement(XmpBasic + "MetadataDate", modified));
        }

        if (!keepsDocumentId)
        {
            description.Add(new XElement(XmpMediaManagement + "DocumentID", "uuid:" + Guid.NewGuid().ToString("D")));
        }

        description.Add(new XElement(XmpMediaManagement + "InstanceID", "uuid:" + Guid.NewGuid().ToString("D")));

        return description;
    }

    private static XElement Alternative(string value)
    {
        return new XElement(
            Rdf + "Alt",
            new XElement(Rdf + "li", new XAttribute(XNamespace.Xml + "lang", "x-default"), value));
    }

    private static string Clean(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];

            if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                builder.Append(character).Append(value[++index]);

                continue;
            }

            if (XmlConvert.IsXmlChar(character))
            {
                builder.Append(character);
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
