namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Represents the result of a tabular data-manipulation command (for example
/// <c>INSERT</c>, <c>UPDATE</c>, <c>DELETE</c>, or <c>ALTER TABLE</c>).
/// </summary>
public sealed class TabularCommandResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TabularCommandResult"/> class.
    /// </summary>
    /// <param name="affectedRows">The number of rows affected by the command.</param>
    public TabularCommandResult(int affectedRows)
        : this(affectedRows, 1)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TabularCommandResult"/> class.
    /// </summary>
    /// <param name="affectedRows">The total number of rows affected across all statements.</param>
    /// <param name="statementCount">The number of statements that were executed.</param>
    public TabularCommandResult(int affectedRows, int statementCount)
    {
        AffectedRows = affectedRows;
        StatementCount = statementCount;
    }

    /// <summary>
    /// Gets the number of rows affected by the command. Schema commands such as
    /// <c>ALTER TABLE</c> report <c>0</c>.
    /// </summary>
    public int AffectedRows { get; }

    /// <summary>
    /// Gets the number of statements that were executed as part of the command batch.
    /// </summary>
    public int StatementCount { get; }
}
