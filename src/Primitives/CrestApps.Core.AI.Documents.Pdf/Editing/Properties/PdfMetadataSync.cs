using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Saves an edited PDF so its XMP metadata agrees with its document information and keeps what the
/// original packet declared.
/// </summary>
/// <remarks>
/// The original packet is taken before the document is changed, so the stale copy is not written, and the
/// document is saved with a packet built from its document information that keeps the PDF/A and PDF/UA
/// identification the file had.
/// </remarks>
internal sealed class PdfMetadataSync
{
    // A document opened for editing is captured once; a tool that captures it again gets the same capture,
    // which still holds the packet the file had.
    private static readonly ConditionalWeakTable<PdfDocument, PdfMetadataSync> _captures = [];

    private XElement _original;
    private bool _keepOtherProperties;

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
    /// Gets a value indicating whether the document was saved with the packet this class builds.
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

        if (_captures.TryGetValue(document, out var existing))
        {
            existing._keepOtherProperties &= keepOtherProperties;

            return existing;
        }

        var catalog = document.Catalog;
        XElement original;

        try
        {
            original = catalog.Elements.GetDictionary("/Metadata")?.Stream?.UnfilteredValue is { } packet
                ? PdfXmpPacket.Parse(packet)
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            original = null;
        }

        catalog.Elements.Remove("/Metadata");

        var sync = new PdfMetadataSync(original, keepOtherProperties);

        _captures.AddOrUpdate(document, sync);

        return sync;
    }

    /// <summary>
    /// Makes the saved packet identify the file as PDF/A, replacing any identification it had.
    /// </summary>
    /// <param name="part">The PDF/A part, such as 2.</param>
    /// <param name="conformance">The conformance level, such as <c>B</c>.</param>
    public void ClaimPdfA(int part, string conformance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conformance);

        var rdf = _original is null
            ? new XElement(PdfXmpPacket.Rdf + "RDF")
            : new XElement(_original);

        foreach (var property in rdf.Descendants().Where(element => element.Name.Namespace == PdfXmpPacket.PdfAId).ToList())
        {
            property.Remove();
        }

        foreach (var attribute in rdf.Descendants().Attributes().Where(attribute => attribute.Name.Namespace == PdfXmpPacket.PdfAId).ToList())
        {
            attribute.Remove();
        }

        rdf.Add(new XElement(
            PdfXmpPacket.Rdf + "Description",
            new XAttribute(PdfXmpPacket.Rdf + "about", string.Empty),
            new XAttribute(XNamespace.Xmlns + "pdfaid", PdfXmpPacket.PdfAId.NamespaceName),
            new XElement(PdfXmpPacket.PdfAId + "part", part.ToString(CultureInfo.InvariantCulture)),
            new XElement(PdfXmpPacket.PdfAId + "conformance", conformance.Trim().ToUpperInvariant())));

        _original = rdf;
        DropPdfAConformance = false;
    }

    /// <summary>
    /// Saves the document, dating the change, with a consistent XMP packet as its metadata stream.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="now">The time of the change.</param>
    /// <param name="security">Encrypts the file as it is written, or <see langword="null"/> to write it unencrypted.</param>
    /// <returns>The file.</returns>
    public byte[] Save(PdfDocument document, DateTimeOffset now, IPdfObjectEncryptor security = null)
    {
        Apply(document, now);

        return PdfObjectWriter.Write(document, security);
    }

    /// <summary>
    /// Dates the change and gives the document a consistent XMP packet as its metadata stream, without saving
    /// it, for a tool that writes the file itself.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="now">The time of the change.</param>
    public void Apply(PdfDocument document, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info.ModificationDate = now.ToUniversalTime();

        var info = document.Info.Dictionary;
        var values = new PdfXmpValues
        {
            Title = Text(info, "/Title"),
            Author = Text(info, "/Author"),
            Subject = Text(info, "/Subject"),
            Keywords = Text(info, "/Keywords"),
            Creator = Text(info, "/Creator"),
            Producer = Text(info, "/Producer"),
            CreationDate = Text(info, "/CreationDate"),
            ModificationDate = Text(info, "/ModDate"),
            Language = Text(document.Catalog, "/Lang"),
        };

        // PDF/A wants the packet readable without decoding, so it is stored as it is.
        var metadata = new PdfDictionary(document);

        metadata.Elements.SetName("/Type", "/Metadata");
        metadata.Elements.SetName("/Subtype", "/XML");
        metadata.CreateStream(PdfXmpPacket.Build(_original, values, _keepOtherProperties, DropPdfAConformance));
        document.Catalog.Elements.SetReference("/Metadata", metadata);
        Rewritten = true;
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

    private static string Text(PdfDictionary dictionary, string key)
    {
        var text = dictionary?.Elements.GetString(key);

        return string.IsNullOrEmpty(text)
            ? null
            : text;
    }
}
