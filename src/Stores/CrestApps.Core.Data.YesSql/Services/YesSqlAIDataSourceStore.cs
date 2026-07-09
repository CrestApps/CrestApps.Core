using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.DataSources;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlAIDataSourceStore : DocumentCatalog<AIDataSource, AIDataSourceIndex>, IAIDataSourceStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlAIDataSourceStore"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="options">The options.</param>
    public YesSqlAIDataSourceStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AICollectionName)
    {
    }

    /// <summary>
    /// Gets AI data sources for the specified source.
    /// </summary>
    /// <param name="source">The source identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyCollection<AIDataSource>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        return (await Session.Query<AIDataSource, AIDataSourceIndex>(x => x.Source == source, collection: CollectionName).ListAsync(cancellationToken)).ToArray();
    }
}
