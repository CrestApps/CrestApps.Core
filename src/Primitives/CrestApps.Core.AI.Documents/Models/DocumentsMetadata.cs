using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Models;

/// <summary>
/// Metadata stored on <see cref="AIProfile.Properties"/> to track
/// documents attached to the profile for RAG functionality.
/// </summary>
public sealed class DocumentsMetadata
{
    /// <summary>
    /// Gets or sets the collection of attached document metadata.
    /// </summary>
    public IList<ChatDocumentInfo> Documents { get; set; } = [];

    /// <summary>
    /// Gets or sets the number of top matching document chunks to include in AI context.
    /// Default is 3 if not specified.
    /// </summary>
    public int? DocumentTopN { get; set; }

    /// <summary>
    /// Gets or sets how retrieved document matches are added to AI context.
    /// </summary>
    public DocumentRetrievalMode? RetrievalMode { get; set; }

    /// <summary>
    /// Gets or sets how much extracted text an uploaded document may hold and still be indexed, or
    /// <see langword="null"/> to use the site's own limit.
    /// </summary>
    public int? MaxIndexableCharacters { get; set; }

    /// <summary>
    /// Gets or sets whether figures in an uploaded document are described by a vision model, or
    /// <see langword="null"/> to follow the host's own setting.
    /// </summary>
    /// <remarks>
    /// Describing figures is the expensive half of ingestion, and whether it is worth paying for depends on
    /// the documents a profile actually receives: a profile handling scanned drawings wants every figure
    /// read, one handling meeting notes is paying for nothing. A profile serves many sessions, so this is
    /// where that choice belongs. With it off, a figure still keeps its caption -- the picture is not lost,
    /// only the model's description of it.
    /// </remarks>
    public bool? DescribeFiguresInUploads { get; set; }
}
