using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// One typed piece of knowledge produced by ingesting a file: the document itself, an article inside it, a
/// chunk of its text, a figure, a chart or a table.
/// </summary>
/// <remarks>
/// Every type lives in this one entry rather than in six of its own, with the type-specific detail carried in
/// <see cref="CrestApps.Core.Abstractions.ExtensibleEntity.Properties"/>. Six entries would mean six models,
/// six handlers, six stores of each kind, six index tables and six sets of registrations, for records that
/// differ in a handful of fields.
/// <para>
/// The owning AI data source identifier is stored in
/// <see cref="CrestApps.Core.Models.SourceCatalogEntry.Source"/>, the same convention the crawl-state records
/// use for their owning crawler, so the records can be queried through the source-catalog abstraction.
/// </para>
/// </remarks>
public sealed class KnowledgeObject : SourceCatalogEntry, IModifiedUtcAwareModel, ICloneable<KnowledgeObject>
{
    /// <summary>
    /// Gets or sets the canonical identifier, which is unique within the data source and stable across
    /// re-ingesting the same bytes.
    /// </summary>
    public string CanonicalId { get; set; }

    /// <summary>
    /// Gets or sets what kind of knowledge this is. See
    /// <see cref="CrestApps.Core.Infrastructure.Indexing.KnowledgeObjectTypes"/>.
    /// </summary>
    public string ObjectType { get; set; }

    /// <summary>
    /// Gets or sets the canonical identifier of the document this ultimately belongs to.
    /// </summary>
    public string RootId { get; set; }

    /// <summary>
    /// Gets or sets the canonical identifier of the object this hangs directly off, so a hit on a chunk of
    /// text can be widened to the article it came from.
    /// </summary>
    public string ParentId { get; set; }

    /// <summary>
    /// Gets or sets the indexer that produced this, or <see langword="null"/> for a manual upload.
    /// </summary>
    public string IndexerId { get; set; }

    /// <summary>
    /// Gets or sets the identifier the connector knows the source item by, such as a URL, a blob name or a
    /// path. It is <see langword="null"/> for a manual upload.
    /// </summary>
    public string SourceItemId { get; set; }

    /// <summary>
    /// Gets or sets the title used for citation.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the text that gets embedded. It is produced already inside the chunk budget, so nothing
    /// downstream re-chunks it.
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// Gets or sets the BCP-47 language tag of the content, when it is known.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets or sets the first page this was read from.
    /// </summary>
    public int? PageStart { get; set; }

    /// <summary>
    /// Gets or sets the last page this was read from.
    /// </summary>
    public int? PageEnd { get; set; }

    /// <summary>
    /// Gets or sets the page number printed on the page, which is not the same as its position in the file.
    /// A citation of "p. 85" has to come from this, never from the page index.
    /// </summary>
    public string Folio { get; set; }

    /// <summary>
    /// Gets or sets the position of this object among its siblings.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>
    /// Gets or sets the hash of the figure bytes, or of the content for everything else.
    /// </summary>
    public string ContentHash { get; set; }

    /// <summary>
    /// Gets or sets the media type of the stored bytes. Figures only.
    /// </summary>
    public string MediaType { get; set; }

    /// <summary>
    /// Gets or sets where the bytes were stored. Figures only.
    /// </summary>
    public string StoragePath { get; set; }

    /// <summary>
    /// Gets or sets how far along this object is. See <see cref="KnowledgeObjectStatus"/>.
    /// </summary>
    public string Status { get; set; } = KnowledgeObjectStatus.Ready;

    /// <summary>
    /// Gets or sets when the object was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets when the object was last modified.
    /// </summary>
    public DateTime? ModifiedUtc { get; set; }

    /// <summary>
    /// Creates a copy of this object.
    /// </summary>
    /// <returns>The copy.</returns>
    public KnowledgeObject Clone()
    {
        return new KnowledgeObject
        {
            ItemId = ItemId,
            Source = Source,
            CanonicalId = CanonicalId,
            ObjectType = ObjectType,
            RootId = RootId,
            ParentId = ParentId,
            IndexerId = IndexerId,
            SourceItemId = SourceItemId,
            Title = Title,
            Content = Content,
            Language = Language,
            PageStart = PageStart,
            PageEnd = PageEnd,
            Folio = Folio,
            Ordinal = Ordinal,
            ContentHash = ContentHash,
            MediaType = MediaType,
            StoragePath = StoragePath,
            Status = Status,
            CreatedUtc = CreatedUtc,
            ModifiedUtc = ModifiedUtc,
            Properties = Properties.Clone(),
        };
    }
}

/// <summary>
/// How far along a knowledge object is.
/// </summary>
public static class KnowledgeObjectStatus
{
    /// <summary>
    /// Everything that was going to be done to it has been done. It is searchable as it stands.
    /// </summary>
    public const string Ready = "Ready";

    /// <summary>
    /// The object is searchable by its caption, and a description is still to be produced for it. Text is
    /// never held back waiting for a figure.
    /// </summary>
    public const string PendingDescription = "PendingDescription";

    /// <summary>
    /// Enrichment failed and will not be retried on its own. The object keeps whatever it already had.
    /// </summary>
    public const string Failed = "Failed";

    /// <summary>
    /// The object is kept so the document stays complete, and kept out of the index so it can never be
    /// returned as an answer. An advertisement between two articles is the case this exists for.
    /// </summary>
    public const string Excluded = "Excluded";
}
