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
    {
        AffectedRows = affectedRows;
    }

    /// <summary>
    /// Gets the number of rows affected by the command. Schema commands such as
    /// <c>ALTER TABLE</c> report <c>0</c>.
    /// </summary>
    public int AffectedRows { get; }
}
