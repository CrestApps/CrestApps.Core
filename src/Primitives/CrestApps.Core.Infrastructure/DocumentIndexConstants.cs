using CrestApps.Core.Infrastructure.Indexing;

namespace CrestApps.Core.Infrastructure;

/// <summary>
/// Constants for document chunk index field names used by
/// <see cref="IVectorSearchService"/> and <see cref="ISearchDocumentManager"/> implementations.
/// </summary>
public static class DocumentIndexConstants
{
    /// <summary>
    /// Provides functionality for column Names.
    /// </summary>
    public static class ColumnNames
    {
        public const string ChunkId = "chunkId";

        public const string Content = "content";

        public const string DocumentId = "documentId";

        public const string FileName = "fileName";

        public const string ReferenceId = "referenceId";

        public const string ReferenceType = "referenceType";

        public const string Embedding = "embedding";

        public const string ChunkIndex = "chunkIndex";
    }
}
