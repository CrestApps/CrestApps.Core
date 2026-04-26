using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.Indexing;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlAIDocumentChunkStore : DocumentCatalog<AIDocumentChunk, AIDocumentChunkIndex>, IAIDocumentChunkStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlAIDocumentChunkStore"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="options">The options.</param>
    public YesSqlAIDocumentChunkStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AIDocsCollectionName)
    {
    }

    /// <summary>
    /// Gets chunks by ai document id.
    /// </summary>
    /// <param name="documentId">The document id.</param>
    public async Task<IReadOnlyCollection<AIDocumentChunk>> GetChunksByAIDocumentIdAsync(string documentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);

        var chunks = await Session.Query<AIDocumentChunk, AIDocumentChunkIndex>(x =>
            x.AIDocumentId == documentId, collection: CollectionName).ListAsync();

return chunks.ToArray();
    }

    /// <summary>
    /// Gets chunks by reference.
    /// </summary>
    /// <param name="referenceId">The reference id.</param>
    /// <param name="referenceType">The reference type.</param>
    public async Task<IReadOnlyCollection<AIDocumentChunk>> GetChunksByReferenceAsync(string referenceId, string referenceType)
    {
        ArgumentException.ThrowIfNullOrEmpty(referenceId);
        ArgumentException.ThrowIfNullOrEmpty(referenceType);

        var chunks = await Session.Query<AIDocumentChunk, AIDocumentChunkIndex>(x =>
            x.ReferenceId == referenceId && x.ReferenceType == referenceType, collection: CollectionName).ListAsync();

return chunks.ToArray();
    }

    /// <summary>
    /// Deletes by document id.
    /// </summary>
    /// <param name="documentId">The document id.</param>
    public async Task DeleteByDocumentIdAsync(string documentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);

        var chunks = await Session.Query<AIDocumentChunk, AIDocumentChunkIndex>(x =>
            x.AIDocumentId == documentId, collection: CollectionName).ListAsync();

        foreach (var chunk in chunks)
        {
            Session.Delete(chunk, CollectionName);
        }
    }
}
