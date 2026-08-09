using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// The default multi-source documentation source catalog. Aggregates entries from all registered
/// <see cref="INamedSourceCatalogSource{T}"/> implementations (for example YesSql or EntityCore stores),
/// deduplicating by name so database-defined sources can be managed alongside custom catalog sources.
/// </summary>
public sealed class DefaultDocumentationSourceCatalog : MultiSourceNamedSourceCatalog<DocumentationSourceEntry>, IDocumentationSourceCatalog
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDocumentationSourceCatalog"/> class.
    /// </summary>
    /// <param name="sources">The registered catalog sources.</param>
    public DefaultDocumentationSourceCatalog(IEnumerable<INamedSourceCatalogSource<DocumentationSourceEntry>> sources)
        : base(sources)
    {
    }

    /// <inheritdoc />
    protected override string GetItemId(DocumentationSourceEntry entry) => entry.ItemId;

    /// <inheritdoc />
    protected override IEnumerable<DocumentationSourceEntry> ApplyFilters(QueryContext context, IEnumerable<DocumentationSourceEntry> entries)
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
            entries = entries.Where(entry => entry.Name is not null && entry.Name.Contains(context.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (context.Sorted)
        {
            entries = entries.OrderBy(static entry => entry.DisplayText ?? entry.Name, StringComparer.OrdinalIgnoreCase);
        }

        return entries;
    }

    /// <inheritdoc />
    protected override string GetSortKey(DocumentationSourceEntry entry) => entry.DisplayText ?? entry.Name;
}
