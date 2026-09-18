using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Documents.Knowledge.Structure;

/// <summary>
/// What a document says about itself in its own front matter.
/// </summary>
public sealed class PublicationMetadata
{
    /// <summary>
    /// Gets or sets the title of the publication as a whole.
    /// </summary>
    public string PublicationTitle { get; set; }

    /// <summary>
    /// Gets or sets the publisher or issuing body.
    /// </summary>
    public string Publisher { get; set; }

    /// <summary>
    /// Gets or sets the responsible editor.
    /// </summary>
    public string Editor { get; set; }

    /// <summary>
    /// Gets or sets the place of publication.
    /// </summary>
    public string Place { get; set; }

    /// <summary>
    /// Gets or sets the volume, exactly as printed.
    /// </summary>
    public string Volume { get; set; }

    /// <summary>
    /// Gets or sets the issue or part number, exactly as printed.
    /// </summary>
    public string Issue { get; set; }

    /// <summary>
    /// Gets or sets the date or period the issue covers, exactly as printed.
    /// </summary>
    public string Date { get; set; }

    /// <summary>
    /// Gets or sets the ISSN or ISBN, when one is printed.
    /// </summary>
    public string Identifier { get; set; }

    /// <summary>
    /// Gets a value indicating whether anything at all was found.
    /// </summary>
    public bool HasValue =>
        !string.IsNullOrWhiteSpace(PublicationTitle) ||
        !string.IsNullOrWhiteSpace(Publisher) ||
        !string.IsNullOrWhiteSpace(Issue) ||
        !string.IsNullOrWhiteSpace(Date);
}

/// <summary>
/// Reads what a document says about itself.
/// </summary>
/// <remarks>
/// A citation that names the file a document arrived in is useless to anyone who has to find the source
/// again. The publication, the issue and the date are what a reader actually needs, and they are printed on
/// the first page or two of almost every document that has them.
/// <para>
/// This costs one model call per ingest and must never fail one. Nothing found simply means a citation
/// names the file, which is what it did before.
/// </para>
/// </remarks>
public interface IPublicationMetadataExtractor
{
    /// <summary>
    /// Reads a document's front matter.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the front matter said, or <see langword="null"/> when it said nothing.</returns>
    Task<PublicationMetadata> ExtractAsync(IngestionDocument document, CancellationToken cancellationToken = default);
}
