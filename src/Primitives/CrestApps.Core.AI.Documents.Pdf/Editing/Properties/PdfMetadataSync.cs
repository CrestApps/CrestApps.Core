using System.Globalization;
using System.Text;
using System.Xml.Linq;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PdfSharp.Pdf;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using PigParsingOptions = UglyToad.PdfPig.ParsingOptions;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Saves an edited PDF so its XMP metadata agrees with its document information and keeps what the
/// original packet declared.
/// </summary>
/// <remarks>
/// PDFsharp replaces a file's XMP packet on every save with one it generates from the information
/// dictionary: it drops the PDF/A and PDF/UA identification a file had, does not escape the values (a
/// title with an ampersand makes the packet unreadable), dates the modification with the creation date,
/// and leaves the replaced packet in the file. This takes the original packet before the save, so the stale
/// copy is not written, and afterwards writes a correct packet in place of PDFsharp's. That packet is the
/// last object PDFsharp writes, so replacing it moves nothing but the cross-reference table. When the saved
/// file is encrypted, or does not have the expected shape, PDFsharp's packet is left as it is.
/// </remarks>
internal sealed class PdfMetadataSync
{
    private readonly XElement _original;
    private readonly bool _keepOtherProperties;

    private PdfMetadataSync(XElement original, bool keepOtherProperties)
    {
        _original = original;
        _keepOtherProperties = keepOtherProperties;
        Conformance = PdfXmpPacket.DescribeConformance(original);
    }

    /// <summary>
    /// Gets the conformance the original packet claimed, such as <c>PDF/A-2B</c>.
    /// </summary>
    public IReadOnlyList<string> Conformance { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the PDF/A identification is dropped because the edit breaks
    /// what it claims.
    /// </summary>
    public bool DropPdfAConformance { get; set; }

    /// <summary>
    /// Gets a value indicating whether the last save wrote the packet this class builds; otherwise the file
    /// carries PDFsharp's own.
    /// </summary>
    public bool Rewritten { get; private set; }

    /// <summary>
    /// Gets the PDF/A part the original packet claimed, or 0.
    /// </summary>
    public int PdfAPart
    {
        get
        {
            var part = PdfXmpPacket.ReadProperty(_original, PdfXmpPacket.PdfAId, "part");

            return int.TryParse(part?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : 0;
        }
    }

    /// <summary>
    /// Takes a document's XMP packet before it is edited, and removes it from the document so the stale
    /// copy is not written.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <param name="keepOtherProperties">Whether properties other than those mirroring the document information are kept; when <see langword="false"/> only the PDF/A and PDF/UA identification is.</param>
    /// <returns>The capture.</returns>
    public static PdfMetadataSync Capture(PdfDocument document, bool keepOtherProperties = true)
    {
        ArgumentNullException.ThrowIfNull(document);

        var catalog = document.Internals.Catalog;
        XElement original = null;

        if (PdfObjects.Resolve(catalog.Elements["/Metadata"]) is PdfDictionary metadata && metadata.Stream is not null)
        {
            try
            {
                original = PdfXmpPacket.Parse(metadata.Stream.UnfilteredValue);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                original = null;
            }
        }

        catalog.Elements.Remove("/Metadata");

        return new PdfMetadataSync(original, keepOtherProperties);
    }

    /// <summary>
    /// Saves the document, dating the change, and writes a consistent XMP packet.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="now">The time of the change.</param>
    /// <returns>The file.</returns>
    public byte[] Save(PdfDocument document, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info.ModificationDate = now.UtcDateTime;

        // PDFsharp refuses to count the pages of a document it has saved.
        var pageCount = document.PageCount;
        var saved = PdfFiles.Save(document);
        var rewritten = Rewrite(saved, document, pageCount);

        Rewritten = rewritten is not null;

        return rewritten ?? saved;
    }

    /// <summary>
    /// Describes what happened to the conformance the file claimed, for an answer.
    /// </summary>
    /// <returns>A sentence, or <see langword="null"/> when there is nothing to say.</returns>
    public string DescribeConformance()
    {
        if (Conformance.Count == 0)
        {
            return null;
        }

        var claims = string.Join(" and ", Conformance);

        if (!Rewritten)
        {
            return $"The file claimed {claims}; that identification could not be kept in this file's metadata, so it no longer makes the claim.";
        }

        if (DropPdfAConformance && PdfAPart > 0)
        {
            return $"The file claimed {claims}; this change is not allowed by that PDF/A part, so the PDF/A identification was removed.";
        }

        return $"The file's {claims} identification was kept. Check it with validate_pdf_compliance if conformance matters.";
    }

    private byte[] Rewrite(byte[] saved, PdfDocument document, int pageCount)
    {
        var reference = document.Internals.Catalog.Elements.GetReference("/Metadata");

        if (reference is null)
        {
            return null;
        }

        var text = Encoding.Latin1.GetString(saved);
        var trailerAt = text.LastIndexOf("\ntrailer", StringComparison.Ordinal);

        if (trailerAt < 0 || text.IndexOf("/Encrypt", trailerAt, StringComparison.Ordinal) >= 0)
        {
            return null;
        }

        var xrefAt = text.LastIndexOf("\nxref", trailerAt, StringComparison.Ordinal);
        var header = string.Create(CultureInfo.InvariantCulture, $"\n{reference.ObjectNumber} {reference.GenerationNumber} obj");
        var objectAt = xrefAt < 0 ? -1 : text.LastIndexOf(header, xrefAt, StringComparison.Ordinal);

        if (objectAt < 0)
        {
            return null;
        }

        var endAt = text.IndexOf("endobj", objectAt, StringComparison.Ordinal);

        // The packet has to be the last object before the cross-reference table, so nothing else moves.
        if (endAt < 0 || endAt > xrefAt || !string.IsNullOrWhiteSpace(text[(endAt + "endobj".Length)..(xrefAt + 1)]))
        {
            return null;
        }

        var startXrefAt = text.LastIndexOf("startxref", StringComparison.Ordinal);

        if (startXrefAt < trailerAt)
        {
            return null;
        }

        var packet = PdfXmpPacket.Build(_original, ReadValues(document), _keepOtherProperties, DropPdfAConformance);
        var objectHead = string.Create(
            CultureInfo.InvariantCulture,
            $"{reference.ObjectNumber} {reference.GenerationNumber} obj\n<<\n  /Type /Metadata\n  /Subtype /XML\n  /Length {packet.Length}\n>>\nstream\n");

        using var output = new MemoryStream(saved.Length + packet.Length);

        output.Write(saved, 0, objectAt + 1);
        output.Write(Encoding.ASCII.GetBytes(objectHead));
        output.Write(packet);
        output.Write("\nendstream\nendobj\n"u8);

        var newXrefAt = output.Position;
        var tableEnd = startXrefAt + "startxref".Length;

        output.Write(saved, xrefAt + 1, tableEnd - (xrefAt + 1));
        output.Write(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"\n{newXrefAt}\n%%EOF\n")));

        var result = output.ToArray();

        return Readable(result, pageCount) ? result : null;
    }

    private static PdfXmpValues ReadValues(PdfDocument document)
    {
        var info = document.Info;

        return new PdfXmpValues
        {
            Title = PdfObjects.GetText(info, "/Title"),
            Author = PdfObjects.GetText(info, "/Author"),
            Subject = PdfObjects.GetText(info, "/Subject"),
            Keywords = PdfObjects.GetText(info, "/Keywords"),
            Creator = PdfObjects.GetText(info, "/Creator"),
            Producer = PdfObjects.GetText(info, "/Producer"),
            CreationDate = PdfObjects.GetText(info, "/CreationDate"),
            ModificationDate = PdfObjects.GetText(info, "/ModDate"),
            Language = PdfObjects.GetText(document.Internals.Catalog, "/Lang"),
        };
    }

    private static bool Readable(byte[] bytes, int pageCount)
    {
        try
        {
            using var document = PigDocument.Open(bytes, new PigParsingOptions { UseLenientParsing = false });

            return document.NumberOfPages == pageCount && document.TryGetXmpMetadata(out _);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
