namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Options that control the lifecycle and limits of in-memory tabular data workspaces.
/// </summary>
public sealed class TabularWorkspaceOptions
{
    /// <summary>
    /// Gets or sets the backstop timeout after which an idle in-memory database is disposed even
    /// if its request never released it (for example because an invocation scope was not disposed).
    /// Normal disposal happens when the prompt completes; this only guards against leaks. The
    /// lightweight rebuild journal is retained so the dataset can be reconstructed on the next
    /// access. Default is 10 minutes.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets how long a workspace may remain idle before it is removed entirely,
    /// including its rebuild journal. Must be greater than <see cref="IdleTimeout"/>.
    /// Default is 2 hours.
    /// </summary>
    public TimeSpan JournalRetention { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Gets or sets how frequently idle workspaces are swept for eviction. Default is 2 minutes.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(2);

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
