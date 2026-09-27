using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Puts back the XMP metadata PDFsharp discards when it saves a file.
/// </summary>
/// <remarks>
/// PDFsharp writes a fresh XMP packet from the document information dictionary every time it saves, so
/// whatever else the original packet said — above all the PDF/A and PDF/UA identification a conforming file
/// must carry — is lost. This copies every property of the original packet that PDFsharp does not write
/// itself into PDFsharp's packet, and appends the result as an incremental update that replaces only the
/// metadata stream, leaving the rest of the saved file untouched.
/// </remarks>
internal static partial class PdfXmpPreservation
{
    // The namespaces PDFsharp writes itself; their properties come from the information dictionary.
    private static readonly HashSet<string> _writtenByPdfSharp = new(StringComparer.Ordinal)
    {
        "http://ns.adobe.com/pdf/1.3/",
        "http://purl.org/dc/elements/1.1/",
        "http://ns.adobe.com/xap/1.0/",
        "http://ns.adobe.com/xap/1.0/mm/",
    };

    /// <summary>
    /// Returns whether an XMP packet holds properties PDFsharp would not write back.
    /// </summary>
    /// <param name="original">The original packet.</param>
    /// <returns><see langword="true"/> when saving with PDFsharp would lose something.</returns>
    public static bool HasForeignProperties(byte[] original)
    {
        return ForeignProperties(original).Count > 0;
    }

    /// <summary>
    /// Restores the foreign properties of the original packet in a file PDFsharp saved.
    /// </summary>
    /// <param name="saved">The file as PDFsharp saved it.</param>
    /// <param name="original">The original XMP packet.</param>
    /// <param name="restored">The file with the properties restored, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the properties were restored; otherwise the saved file is left as it is.</returns>
    public static bool TryRestore(byte[] saved, byte[] original, out byte[] restored)
    {
        ArgumentNullException.ThrowIfNull(saved);

        restored = null;

        var foreign = ForeignProperties(original);

        if (foreign.Count == 0)
        {
            return false;
        }

        try
        {
            int objectNumber;
            int generation;
            byte[] packet;

            using (var document = PdfReader.Open(new MemoryStream(saved, writable: false), PdfDocumentOpenMode.Import))
            {
                if (document.Internals.Catalog.Elements["/Metadata"] is not PdfReference reference)
                {
                    return false;
                }

                objectNumber = reference.ObjectNumber;
                generation = reference.GenerationNumber;

                var current = PdfObjects.GetDictionary(document.Internals.Catalog, "/Metadata")?.Stream?.UnfilteredValue;

                if (current is null)
                {
                    return false;
                }

                packet = Merge(current, foreign);
            }

            restored = AppendUpdate(saved, objectNumber, generation, packet);

            return restored is not null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            restored = null;

            return false;
        }
    }

    private static List<(XName Name, XElement Element, XAttribute Attribute)> ForeignProperties(byte[] original)
    {
        var properties = new List<(XName Name, XElement Element, XAttribute Attribute)>();

        if (original is null || original.Length == 0)
        {
            return properties;
        }

        XDocument document;

        try
        {
            document = PdfXmpInfo.Load(original);
        }
        catch (XmlException)
        {
            return properties;
        }

        foreach (var description in document.Descendants(PdfXmpInfo.Rdf + "Description"))
        {
            foreach (var attribute in description.Attributes())
            {
                if (attribute.IsNamespaceDeclaration ||
                    attribute.Name.Namespace == PdfXmpInfo.Rdf ||
                    attribute.Name.Namespace == XNamespace.None ||
                    _writtenByPdfSharp.Contains(attribute.Name.NamespaceName))
                {
                    continue;
                }

                properties.Add((attribute.Name, null, attribute));
            }

            foreach (var element in description.Elements())
            {
                if (!_writtenByPdfSharp.Contains(element.Name.NamespaceName))
                {
                    properties.Add((element.Name, element, null));
                }
            }
        }

        return properties;
    }

    private static byte[] Merge(byte[] current, List<(XName Name, XElement Element, XAttribute Attribute)> foreign)
    {
        var document = PdfXmpInfo.Load(current);
        var rdf = document.Descendants(PdfXmpInfo.Rdf + "RDF").FirstOrDefault()
            ?? throw new InvalidOperationException("The saved metadata has no RDF element.");

        foreach (var group in foreign.GroupBy(property => property.Name.Namespace))
        {
            var description = new XElement(
                PdfXmpInfo.Rdf + "Description",
                new XAttribute(PdfXmpInfo.Rdf + "about", string.Empty));

            foreach (var (name, element, attribute) in group)
            {
                if (document.Descendants(name).Any())
                {
                    continue;
                }

                description.Add(element is not null
                    ? new XElement(element)
                    : new XElement(name, attribute.Value));
            }

            if (description.HasElements)
            {
                rdf.Add(description);
            }
        }

        var xml = document.ToString(SaveOptions.DisableFormatting);

        // The packet keeps the processing instructions it was parsed with.
        var packet = xml.Contains("<?xpacket begin", StringComparison.Ordinal)
            ? xml
            : "<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>" + xml + "<?xpacket end=\"w\"?>";

        return Encoding.UTF8.GetBytes(packet);
    }

    private static byte[] AppendUpdate(byte[] saved, int objectNumber, int generation, byte[] packet)
    {
        var text = Encoding.Latin1.GetString(saved);
        var trailerAt = text.LastIndexOf("trailer", StringComparison.Ordinal);
        var startXrefAt = text.LastIndexOf("startxref", StringComparison.Ordinal);

        if (trailerAt < 0 || startXrefAt < trailerAt)
        {
            // A file written with a cross-reference stream has no trailer to extend this way.
            return null;
        }

        var trailer = text[trailerAt..startXrefAt];

        if (trailer.Contains("/Encrypt", StringComparison.Ordinal))
        {
            // The new stream would have to be encrypted as well.
            return null;
        }

        var previous = StartXref().Match(text, startXrefAt);
        var size = SizeEntry().Match(trailer);
        var root = RootEntry().Match(trailer);

        if (!previous.Success || !size.Success || !root.Success)
        {
            return null;
        }

        var info = InfoEntry().Match(trailer);
        var id = IdEntry().Match(trailer);

        using var output = new MemoryStream(saved.Length + packet.Length + 512);

        output.Write(saved);

        if (saved.Length > 0 && saved[^1] is not ((byte)'\n' or (byte)'\r'))
        {
            output.WriteByte((byte)'\n');
        }

        var objectOffset = output.Position;

        Write(output, string.Create(CultureInfo.InvariantCulture, $"{objectNumber} {generation} obj\n<</Type/Metadata/Subtype/XML/Length {packet.Length}>>\nstream\n"));
        output.Write(packet);
        Write(output, "\nendstream\nendobj\n");

        var xrefOffset = output.Position;
        var entries = new StringBuilder();

        entries.Append(CultureInfo.InvariantCulture, $"xref\n{objectNumber} 1\n{objectOffset:D10} {generation:D5} n \n");
        entries.Append("trailer\n<<");
        entries.Append(CultureInfo.InvariantCulture, $"/Size {size.Groups[1].Value}");
        entries.Append(CultureInfo.InvariantCulture, $"/Root {root.Groups[1].Value}");

        if (info.Success)
        {
            entries.Append(CultureInfo.InvariantCulture, $"/Info {info.Groups[1].Value}");
        }

        if (id.Success)
        {
            entries.Append(id.Value);
        }

        entries.Append(CultureInfo.InvariantCulture, $"/Prev {previous.Groups[1].Value}>>\nstartxref\n{xrefOffset}\n%%EOF\n");

        Write(output, entries.ToString());

        return output.ToArray();
    }

    private static void Write(MemoryStream output, string text)
    {
        output.Write(Encoding.Latin1.GetBytes(text));
    }

    [GeneratedRegex(@"startxref\s+(\d+)")]
    private static partial Regex StartXref();

    [GeneratedRegex(@"/Size\s+(\d+)")]
    private static partial Regex SizeEntry();

    [GeneratedRegex(@"/Root\s+(\d+\s+\d+\s+R)")]
    private static partial Regex RootEntry();

    [GeneratedRegex(@"/Info\s+(\d+\s+\d+\s+R)")]
    private static partial Regex InfoEntry();

    [GeneratedRegex(@"/ID\s*\[[^\]]*\]")]
    private static partial Regex IdEntry();
}
