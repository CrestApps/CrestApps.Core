using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// A base class that aggregates entries from multiple <see cref="INamedSourceCatalogSource{T}"/>
/// implementations, deduplicating by name (lower-order sources win). Write operations are
/// delegated to the first <see cref="IWritableNamedSourceCatalogSource{T}"/> found.
/// Extends <see cref="MultiSourceNamedCatalog{T}"/> with source-aware lookup methods.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public abstract class MultiSourceNamedSourceCatalog<T> : MultiSourceNamedCatalog<T>, INamedSourceCatalog<T>
    where T : INameAwareModel, ISourceAwareModel
{
    protected MultiSourceNamedSourceCatalog(IEnumerable<INamedSourceCatalogSource<T>> sources)
        : base(sources)
    {
    }

    public async ValueTask<IReadOnlyCollection<T>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        var entries = await GetMergedEntriesAsync(cancellationToken);

        return entries
            .Where(entry => string.Equals(entry.Source, source, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public async ValueTask<T> GetAsync(string name, string source, CancellationToken cancellationToken = default)
    {
        var entries = await GetMergedEntriesAsync(cancellationToken);

        return entries.FirstOrDefault(entry =>
            string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Source, source, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    protected override IEnumerable<T> ApplyFilters(QueryContext context, IEnumerable<T> entries)
    {
        if (context is not null && !string.IsNullOrEmpty(context.Source))
        {
            entries = entries.Where(entry => string.Equals(entry.Source, context.Source, StringComparison.OrdinalIgnoreCase));
        }

        return base.ApplyFilters(context, entries);
    }
}
