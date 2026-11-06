using CrestApps.Core.Infrastructure.Indexing;

namespace CrestApps.Core.Infrastructure;

/// <summary>
/// The field names of a document chunk index, read by the <see cref="IVectorSearchService"/> implementations.
/// </summary>
/// <remarks>
/// Every name defaults to the matching <see cref="DocumentIndexConstants.ColumnNames"/> value. A host whose
/// indexes use other field names passes an instance with those names to the vector search registration, for
/// example <c>new DocumentIndexColumnNames { FileName = "file_name" }</c>.
/// </remarks>
public sealed class DocumentIndexColumnNames
{
    /// <summary>
    /// Gets the field names of <see cref="DocumentIndexConstants.ColumnNames"/>.
    /// </summary>
    public static DocumentIndexColumnNames Default { get; } = new();

    /// <summary>
    /// Gets the name of the field that holds the chunk identifier.
    /// </summary>
    public string ChunkId { get; init; } = DocumentIndexConstants.ColumnNames.ChunkId;

    /// <summary>
    /// Gets the name of the field that holds the chunk text.
    /// </summary>
    public string Content { get; init; } = DocumentIndexConstants.ColumnNames.Content;

    /// <summary>
    /// Gets the name of the field that holds the identifier of the document the chunk was read from.
    /// </summary>
    public string DocumentId { get; init; } = DocumentIndexConstants.ColumnNames.DocumentId;

    /// <summary>
    /// Gets the name of the field that holds the file name of the document.
    /// </summary>
    public string FileName { get; init; } = DocumentIndexConstants.ColumnNames.FileName;

    /// <summary>
    /// Gets the name of the field that holds the identifier of the resource the document belongs to.
    /// </summary>
    public string ReferenceId { get; init; } = DocumentIndexConstants.ColumnNames.ReferenceId;

    /// <summary>
    /// Gets the name of the field that holds the type of the resource the document belongs to.
    /// </summary>
    public string ReferenceType { get; init; } = DocumentIndexConstants.ColumnNames.ReferenceType;

    /// <summary>
    /// Gets the name of the field that holds the chunk embedding.
    /// </summary>
    public string Embedding { get; init; } = DocumentIndexConstants.ColumnNames.Embedding;

    /// <summary>
    /// Gets the name of the field that holds the position of the chunk in its document.
    /// </summary>
    public string ChunkIndex { get; init; } = DocumentIndexConstants.ColumnNames.ChunkIndex;
}
