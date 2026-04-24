using CrestApps.Core.Data.YesSql.Indexes;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

public class SourceDocumentCatalog<T, TIndex> : DocumentCatalog<T, TIndex>, ISourceCatalog<T>
    where T : CatalogItem, ISourceAwareModel
    where TIndex : CatalogItemIndex, ISourceAwareIndex
{
    public SourceDocumentCatalog(ISession session, string collectionName = null, ILogger<DocumentCatalog<T, TIndex>> logger = null)
        : base(session, collectionName, logger)
    {
    }

    public async ValueTask<IReadOnlyCollection<T>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        return (await Session.Query<T, TIndex>(x => x.Source == source, collection: CollectionName).ListAsync(cancellationToken)).ToArray();
    }
}
