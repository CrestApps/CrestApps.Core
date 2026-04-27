using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents an AI data source that links an index profile to an AI knowledge base
/// for document retrieval and augmented generation.
/// </summary>
public sealed class AIDataSource : CatalogItem, IDisplayTextAwareModel, ICloneable<AIDataSource>
{
    /// <summary>
    /// Gets or sets the legacy profile source value retained for backward compatibility.
    /// </summary>
    [Obsolete("Do not use any more. Instead use SourceIndexProfileName")]
    public string ProfileSource { get; set; }

    /// <summary>
    /// Gets or sets the legacy data source type retained for backward compatibility.
    /// </summary>
    [Obsolete("Do not use any more.")]
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the human-readable display name for this data source.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this data source was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who created this data source.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the owner identifier associated with this data source.
    /// </summary>
    public string OwnerId { get; set; }

    /// <summary>
    /// Gets or sets the name of the source index to query for data.
    /// </summary>
    public string SourceIndexProfileName { get; set; }

    /// <summary>
    /// Gets or sets the name of the AI knowledge base index used to store document embeddings.
    /// </summary>
    public string AIKnowledgeBaseIndexProfileName { get; set; }

    /// <summary>
    /// Gets or sets the source index field name that maps to the document key (reference ID).
    /// When not mapped, the document's native key (_id) is used.
    /// </summary>
    public string KeyFieldName { get; set; }

    /// <summary>
    /// Gets or sets the source index field name that maps to the document title.
    /// </summary>
    public string TitleFieldName { get; set; }

    /// <summary>
    /// Gets or sets the source index field name that maps to the document content (text).
    /// </summary>
    public string ContentFieldName { get; set; }

    /// <summary>
    /// Clones the operation.
    /// </summary>
    public AIDataSource Clone()
    {
        return new AIDataSource
        {
            ItemId = ItemId,
            DisplayText = DisplayText,
            CreatedUtc = CreatedUtc,
#pragma warning disable CS0618 // Type or member is obsolete
            ProfileSource = ProfileSource,
            Type = Type,
#pragma warning restore CS0618 // Type or member is obsolete
            Author = Author,
            OwnerId = OwnerId,
            SourceIndexProfileName = SourceIndexProfileName,
            AIKnowledgeBaseIndexProfileName = AIKnowledgeBaseIndexProfileName,
            KeyFieldName = KeyFieldName,
            TitleFieldName = TitleFieldName,
            ContentFieldName = ContentFieldName,
            Properties = Properties.Clone(),
        };
    }
}
