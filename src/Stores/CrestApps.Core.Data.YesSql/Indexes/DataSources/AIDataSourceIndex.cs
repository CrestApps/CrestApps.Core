using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.DataSources;

/// <summary>
/// YesSql map index for <see cref="AIDataSource"/>, storing the item identifier,
/// display text, and associated search index profile name for efficient data-source queries.
/// </summary>
public sealed class AIDataSourceIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the human-readable display text of the AI data source.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the registered source identifier.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the name of the search index profile that backs this data source.
    /// </summary>
    public string SourceIndexProfileName { get; set; }
}
