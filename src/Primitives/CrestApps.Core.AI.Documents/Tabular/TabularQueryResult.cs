namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Represents the result of a read-only tabular query.
/// </summary>
public sealed class TabularQueryResult
{
    /// <summary>
    /// Gets or sets the column names in result order.
    /// </summary>
    public IReadOnlyList<string> Columns { get; set; } = [];

    /// <summary>
    /// Gets or sets the result rows. Each row is an array of cell values aligned to
    /// <see cref="Columns"/>; a <see langword="null"/> cell represents a SQL <c>NULL</c>.
    /// </summary>
    public IReadOnlyList<string[]> Rows { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the result was truncated to the configured
    /// maximum row limit.
    /// </summary>
    public bool Truncated { get; set; }
}
