namespace CrestApps.Core.Models;

/// <summary>
/// Represents a single page of results returned from a paginated catalog query,
/// containing the total count and the entries for the requested page.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public sealed class PageResult<T>
{
    /// <summary>
    /// Gets or sets the total number of entries across all pages.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets the entries for the current page.
    /// </summary>
    public IReadOnlyCollection<T> Entries { get; set; }
}
