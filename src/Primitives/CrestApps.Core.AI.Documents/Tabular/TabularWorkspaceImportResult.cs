namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Describes the outcome of importing a tabular source directly into a SQLite workspace.
/// </summary>
public sealed class TabularWorkspaceImportResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TabularWorkspaceImportResult"/> class.
    /// </summary>
    /// <param name="columns">The imported table columns.</param>
    /// <param name="rowCount">The imported row count.</param>
    /// <param name="insertCommandCount">The number of executed insert commands.</param>
    /// <param name="rowsPerBatch">The effective rows-per-batch value used during import.</param>
    public TabularWorkspaceImportResult(
        IReadOnlyList<TabularColumnInfo> columns,
        int rowCount,
        int insertCommandCount,
        int rowsPerBatch)
    {
        Columns = columns;
        RowCount = rowCount;
        InsertCommandCount = insertCommandCount;
        RowsPerBatch = rowsPerBatch;
    }

    /// <summary>
    /// Gets the imported table columns.
    /// </summary>
    public IReadOnlyList<TabularColumnInfo> Columns { get; }

    /// <summary>
    /// Gets the imported row count.
    /// </summary>
    public int RowCount { get; }

    /// <summary>
    /// Gets the number of executed insert commands.
    /// </summary>
    public int InsertCommandCount { get; }

    /// <summary>
    /// Gets the effective rows-per-batch value used during import.
    /// </summary>
    public int RowsPerBatch { get; }
}
