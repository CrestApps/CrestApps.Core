namespace CrestApps.Core.Models;

/// <summary>
/// Carries filtering and ordering parameters passed to paginated catalog query methods.
/// Derive from this class to add query-specific filter fields.
/// </summary>
public class QueryContext
{
    /// <summary>
    /// Gets or sets the source or provider name to filter results by.
    /// When <see langword="null"/> or empty, results are not filtered by source.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the entry name to filter results by.
    /// When <see langword="null"/> or empty, results are not filtered by name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether results should be returned in sorted order.
    /// </summary>
    public bool Sorted { get; set; }
}
