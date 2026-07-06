namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// The exception thrown when SQL submitted to a tabular workspace is rejected by validation,
/// for example because it is not a single allowed statement or contains a forbidden keyword.
/// </summary>
public sealed class TabularSqlException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TabularSqlException"/> class.
    /// </summary>
    /// <param name="message">The validation message describing why the SQL was rejected.</param>
    public TabularSqlException(string message)
        : base(message)
    {
    }
}
