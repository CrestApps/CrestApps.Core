using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.Indexing;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlAIDocumentStore : DocumentCatalog<AIDocument, AIDocumentIndex>, IAIDocumentStore
{
    public YesSqlAIDocumentStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AIDocsCollectionName)
    {
    }

    public async Task<IReadOnlyCollection<AIDocument>> GetDocumentsAsync(string referenceId, string referenceType)
    {
        ArgumentException.ThrowIfNullOrEmpty(referenceId);
        ArgumentException.ThrowIfNullOrEmpty(referenceType);

        var docs = await Session.Query<AIDocument, AIDocumentIndex>(x =>
            x.ReferenceId == referenceId && x.ReferenceType == referenceType, collection: CollectionName).ListAsync();

        return docs.ToArray();
    }
}
