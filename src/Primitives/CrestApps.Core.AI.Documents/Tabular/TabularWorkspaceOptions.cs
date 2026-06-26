namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Options that control the limits of the per-prompt in-memory tabular data workspace.
/// </summary>
public sealed class TabularWorkspaceOptions
{
    /// <summary>
    /// Gets or sets the maximum number of rows returned by a single query. The model is told
    /// when results are truncated so it can refine the query (e.g., add aggregation or LIMIT).
    /// Default is 100.
    /// </summary>
    public int MaxRowsPerQuery { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of characters rendered for a single cell value before
    /// it is truncated, keeping tool output compact. Default is 200.
    /// </summary>
    public int MaxCellLength { get; set; } = 200;

    /// <summary>
    /// Gets or sets the SQL command timeout, in seconds, applied to query and command execution.
    /// Default is 30 seconds.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
