using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.FileSources;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

/// <summary>
/// YesSql-backed <see cref="IFileSourceStore"/>.
/// </summary>
public sealed class YesSqlFileSourceStore : DocumentCatalog<FileSource, FileSourceIndex>, IFileSourceStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlFileSourceStore"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="options">The options.</param>
    public YesSqlFileSourceStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AICollectionName)
    {
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyCollection<FileSource>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        return (await Session.Query<FileSource, FileSourceIndex>(x => x.Source == source, collection: CollectionName).ListAsync(cancellationToken)).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<FileSource>> GetByDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);

        return (await Session.Query<FileSource, FileSourceIndex>(x => x.AIDataSourceId == dataSourceId, collection: CollectionName).ListAsync(cancellationToken)).ToArray();
    }
}
