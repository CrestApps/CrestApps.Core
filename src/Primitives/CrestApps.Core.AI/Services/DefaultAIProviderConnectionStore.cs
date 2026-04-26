using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// The default multi-source AI provider connection store. Aggregates entries from all
/// registered <see cref="INamedSourceCatalogSource{T}"/> implementations
/// (configuration, YesSql, EntityCore, or custom sources).
/// </summary>
public sealed class DefaultAIProviderConnectionStore : MultiSourceNamedSourceCatalog<AIProviderConnection>, IAIProviderConnectionStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIProviderConnectionStore"/> class.
    /// </summary>
    /// <param name="sources">The sources.</param>
    public DefaultAIProviderConnectionStore(IEnumerable<INamedSourceCatalogSource<AIProviderConnection>> sources)
        : base(sources)
    {
    }

    /// <summary>
    /// Gets item id.
    /// </summary>
    /// <param name="entry">The entry.</param>
    protected override string GetItemId(AIProviderConnection entry) => entry.ItemId;

    /// <summary>
    /// Applies filters.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="entries">The entries.</param>
    protected override IEnumerable<AIProviderConnection> ApplyFilters(QueryContext context, IEnumerable<AIProviderConnection> entries)
    {
        if (context is null)
        {
            return entries;
        }

        if (!string.IsNullOrEmpty(context.Source))
        {
            entries = entries.Where(entry => string.Equals(entry.Source, context.Source, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(context.Name))
        {
            entries = entries.Where(entry => entry.Name.Contains(context.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (context.Sorted)
        {
            entries = entries.OrderBy(static entry => entry.DisplayText ?? entry.Name, StringComparer.OrdinalIgnoreCase);
        }

        return entries;
    }

    /// <summary>
    /// Gets sort key.
    /// </summary>
    /// <param name="entry">The entry.</param>
    protected override string GetSortKey(AIProviderConnection entry) => entry.DisplayText ?? entry.Name;
}
