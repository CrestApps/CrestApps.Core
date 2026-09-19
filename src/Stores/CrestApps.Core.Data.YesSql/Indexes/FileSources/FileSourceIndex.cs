using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.YesSql.Indexes.FileSources;

/// <summary>
/// YesSql map index for <see cref="FileSource"/>, keyed by the target data source so the source handler can
/// find every file source for a <c>File</c> data source.
/// </summary>
public sealed class FileSourceIndex : CatalogItemIndex
{
    /// <summary>
    /// Gets or sets the human-readable display text of the file source.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the target AI data source identifier.
    /// </summary>
    public string AIDataSourceId { get; set; }

    /// <summary>
    /// Gets or sets the ingestion connector identifier (the file source's source).
    /// </summary>
    public string Source { get; set; }
}
