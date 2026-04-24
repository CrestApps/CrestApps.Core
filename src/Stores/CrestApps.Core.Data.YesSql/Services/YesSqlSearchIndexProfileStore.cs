using CrestApps.Core.Data.YesSql.Indexes.Indexing;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlSearchIndexProfileStore : DocumentCatalog<SearchIndexProfile, SearchIndexProfileIndex>, ISearchIndexProfileStore
{
    public YesSqlSearchIndexProfileStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.DefaultCollectionName)
    {
    }

    public async ValueTask<SearchIndexProfile> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        return await Session.Query<SearchIndexProfile, SearchIndexProfileIndex>(x => x.Name == name, collection: CollectionName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<SearchIndexProfile>> GetByTypeAsync(string type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var items = await Session.Query<SearchIndexProfile, SearchIndexProfileIndex>(x => x.Type == type, collection: CollectionName)
            .ListAsync();

        return items.ToArray();
    }
}
