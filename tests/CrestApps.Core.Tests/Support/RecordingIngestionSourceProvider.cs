using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// An <see cref="IIngestionSourceProvider"/> that records what the run service wrote back.
/// </summary>
/// <remarks>
/// The pipeline reaches a source record only through a contributed provider, so a test that wants to see
/// the run summary supplies one of these in place of whichever feature owns the record.
/// </remarks>
public sealed class RecordingIngestionSourceProvider : IIngestionSourceProvider
{
    /// <summary>
    /// Gets the records this provider was asked to save, in order.
    /// </summary>
    public List<IngestionSource> Saved { get; } = [];

    /// <summary>
    /// Gets the records this provider can find, keyed by identifier.
    /// </summary>
    public Dictionary<string, IngestionSource> Sources { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<IngestionSource> FindByIdAsync(string itemId, CancellationToken cancellationToken = default)
        => Task.FromResult(Sources.TryGetValue(itemId ?? string.Empty, out var source) ? source : null);

    /// <inheritdoc />
    public Task<bool> TryUpdateAsync(IngestionSource ingestionSource, CancellationToken cancellationToken = default)
    {
        Saved.Add(ingestionSource);

        return Task.FromResult(true);
    }
}
