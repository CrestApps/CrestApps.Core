using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// Gives the ingestion pipeline access to <see cref="FileSource"/> records.
/// </summary>
public sealed class FileSourceIngestionSourceProvider : IIngestionSourceProvider
{
    private readonly IFileSourceStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSourceIngestionSourceProvider"/> class.
    /// </summary>
    /// <param name="store">The store file sources live in.</param>
    public FileSourceIngestionSourceProvider(IFileSourceStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public async Task<IngestionSource> FindByIdAsync(string itemId, CancellationToken cancellationToken = default)
        => await _store.FindByIdAsync(itemId, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryUpdateAsync(IngestionSource ingestionSource, CancellationToken cancellationToken = default)
    {
        if (ingestionSource is not FileSource fileSource)
        {
            return false;
        }

        await _store.UpdateAsync(fileSource, cancellationToken);

        return true;
    }
}
