using CrestApps.Core.Data.YesSql.Indexes.Indexing;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlSearchIndexProfileStore : DocumentCatalog<SearchIndexProfile, SearchIndexProfileIndex>, ISearchIndexProfileStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlSearchIndexProfileStore"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="options">The options.</param>
    public YesSqlSearchIndexProfileStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.DefaultCollectionName)
    {
    }

    /// <summary>
    /// Finds by name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<SearchIndexProfile> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        return await Session.Query<SearchIndexProfile, SearchIndexProfileIndex>(x => x.Name == name, collection: CollectionName)
                    .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Gets by type.
    /// </summary>
    /// <param name="type">The type.</param>
    public async Task<IReadOnlyCollection<SearchIndexProfile>> GetByTypeAsync(string type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var items = await Session.Query<SearchIndexProfile, SearchIndexProfileIndex>(x => x.Type == type, collection: CollectionName)
            .ListAsync();

        return items.ToArray();
    }
}
