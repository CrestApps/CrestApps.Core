using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// What one migration pass moved.
/// </summary>
/// <param name="FileSourcesMoved">How many records were moved out of the web crawler store.</param>
/// <param name="ItemStatesMoved">How many per-item state records were moved out of the crawl state store.</param>
public readonly record struct FileSourceMigrationResult(int FileSourcesMoved, int ItemStatesMoved);

/// <summary>
/// Moves file sources out of the web crawler store, where they used to be kept.
/// </summary>
/// <remarks>
/// A file source was once a <see cref="WebCrawler"/> whose source happened to name an ingestion connector
/// rather than a crawl strategy, and its per-item state was a <see cref="WebCrawlState"/> with a file path
/// in a field called <c>Url</c>. Both are their own records now, so a host that has run an older build has
/// rows in the wrong tables until this runs.
/// <para>
/// It goes through the store interfaces rather than SQL, so one implementation serves every persistence
/// provider. It is idempotent: a second pass finds nothing left to move.
/// </para>
/// </remarks>
public static class FileSourceStoreMigration
{
    /// <summary>
    /// Moves every file source, and the item state of everything the ingestion pipeline runs, into their
    /// own stores.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What was moved.</returns>
    /// <remarks>
    /// Call this once at startup, after the stores are registered. It opens its own scope and commits what
    /// it did, so it does not depend on a request or a caller's unit of work.
    /// </remarks>
    public static async Task<FileSourceMigrationResult> MigrateFileSourcesAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();

        var provider = scope.ServiceProvider;
        var webCrawlerStore = provider.GetService<IWebCrawlerStore>();
        var fileSourceStore = provider.GetService<IFileSourceStore>();

        if (webCrawlerStore is null || fileSourceStore is null)
        {
            return default;
        }

        var logger = provider.GetService<ILoggerFactory>()?.CreateLogger(typeof(FileSourceStoreMigration).FullName);
        var connectorNames = ResolveConnectorNames(provider);

        var crawlers = await webCrawlerStore.GetAllAsync(cancellationToken);
        var moved = new List<WebCrawler>();

        foreach (var crawler in crawlers)
        {
            if (string.IsNullOrWhiteSpace(crawler.Source) || !connectorNames.Contains(crawler.Source))
            {
                continue;
            }

            await fileSourceStore.CreateAsync(ToFileSource(crawler), cancellationToken);
            await webCrawlerStore.DeleteAsync(crawler, cancellationToken);

            moved.Add(crawler);
        }

        var statesMoved = await MigrateItemStatesAsync(
            provider,
            moved,
            crawlers.Where(crawler => !moved.Contains(crawler)),
            cancellationToken);

        if (moved.Count > 0 || statesMoved > 0)
        {
            var committer = provider.GetService<IStoreCommitter>();

            if (committer is not null)
            {
                await committer.CommitAsync(cancellationToken);
            }

            if (logger is not null && logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Moved {FileSourceCount} file source(s) and {StateCount} item state record(s) out of the web crawler stores.",
                    moved.Count,
                    statesMoved);
            }
        }

        return new FileSourceMigrationResult(moved.Count, statesMoved);
    }

    /// <summary>
    /// Moves the per-item state of everything the ingestion pipeline now runs.
    /// </summary>
    /// <param name="provider">The scoped services.</param>
    /// <param name="movedFileSources">The records that have just become file sources.</param>
    /// <param name="remainingCrawlers">The records that are still web crawlers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>How many state records were moved.</returns>
    /// <remarks>
    /// A crawler that feeds an ingested data source is run by the same pipeline as a file source and keeps
    /// its state in the same place, so its rows move too. One pointed at a <c>Web</c> data source belongs to
    /// the re-index service and is left where it is.
    /// </remarks>
    private static async Task<int> MigrateItemStatesAsync(
        IServiceProvider provider,
        IReadOnlyCollection<WebCrawler> movedFileSources,
        IEnumerable<WebCrawler> remainingCrawlers,
        CancellationToken cancellationToken)
    {
        var crawlStateStore = provider.GetService<IWebCrawlStateStore>();
        var itemStateStore = provider.GetService<IIngestionItemStateStore>();

        if (crawlStateStore is null || itemStateStore is null)
        {
            return 0;
        }

        var dataSourceStore = provider.GetService<IAIDataSourceStore>();
        var owners = new List<string>(movedFileSources.Select(fileSource => fileSource.ItemId));

        if (dataSourceStore is not null)
        {
            foreach (var crawler in remainingCrawlers)
            {
                if (string.IsNullOrWhiteSpace(crawler.AIDataSourceId))
                {
                    continue;
                }

                var dataSource = await dataSourceStore.FindByIdAsync(crawler.AIDataSourceId, cancellationToken);

                if (dataSource is not null &&
                    string.Equals(dataSource.Source, AIDataSourceSourceTypes.File, StringComparison.OrdinalIgnoreCase))
                {
                    owners.Add(crawler.ItemId);
                }
            }
        }

        var moved = 0;

        foreach (var ownerId in owners)
        {
            var states = await crawlStateStore.GetAsync(ownerId, cancellationToken);

            if (states.Count == 0)
            {
                continue;
            }

            foreach (var state in states)
            {
                await itemStateStore.CreateAsync(ToItemState(state, ownerId), cancellationToken);

                moved++;
            }

            await crawlStateStore.DeleteByCrawlerIdAsync(ownerId, cancellationToken);
        }

        return moved;
    }

    /// <summary>
    /// Reads the connector names this host registered.
    /// </summary>
    /// <param name="provider">The scoped services.</param>
    /// <returns>The names, compared without regard to case.</returns>
    /// <remarks>
    /// This is the list the File Sources screens already offer, and a crawl strategy is never in it, so it
    /// is exactly the set of sources that identified a file source when the two shared a store.
    /// </remarks>
    private static HashSet<string> ResolveConnectorNames(IServiceProvider provider)
    {
        var options = provider.GetService<IOptions<IngestionConnectorOptions>>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (options is null)
        {
            return names;
        }

        foreach (var connector in options.Value.Connectors)
        {
            if (!string.IsNullOrWhiteSpace(connector.Name))
            {
                names.Add(connector.Name);
            }
        }

        return names;
    }

    /// <summary>
    /// Turns a crawler record into the file source it always was.
    /// </summary>
    /// <param name="crawler">The stored record.</param>
    /// <returns>The file source, keeping the same identifier so nothing that points at it breaks.</returns>
    private static FileSource ToFileSource(WebCrawler crawler)
    {
        var fileSource = new FileSource();

        crawler.CopyTo(fileSource);

        return fileSource;
    }

    /// <summary>
    /// Turns a crawl-state record into an item-state record, giving each value the name it always had.
    /// </summary>
    /// <param name="state">The stored crawl state.</param>
    /// <param name="ownerId">The owning record's identifier.</param>
    /// <returns>The item state.</returns>
    private static IngestionItemState ToItemState(WebCrawlState state, string ownerId)
    {
        return new IngestionItemState
        {
            ItemId = state.ItemId,
            Source = ownerId,

            // Url held whatever the connector knew the item by, which for a folder or a file server was a
            // path; ChangeFrequency held the connector's opaque change token; and ContentHash held the
            // identifier of the document the item produced.
            ItemKey = state.Url,
            ChangeToken = state.ChangeFrequency,
            DocumentRootId = state.ContentHash,
            LastModifiedUtc = state.LastModifiedUtc,
            LastIngestedUtc = state.LastIndexedUtc,
            LastSeenUtc = state.LastSeenUtc,
        };
    }
}
