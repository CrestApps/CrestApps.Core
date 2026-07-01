namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Describes a column within a tabular table loaded into a workspace.
/// </summary>
public sealed class TabularColumnInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TabularColumnInfo"/> class.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <param name="declaredType">The declared SQLite storage type of the column.</param>
    /// <param name="sourceName">The original source header name, when different from the SQL column name.</param>
    public TabularColumnInfo(string name, string declaredType, string sourceName = null)
    {
        Name = name;
        DeclaredType = declaredType;
        SourceName = sourceName;
    }

    /// <summary>
    /// Gets the column name as used in SQL.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the declared SQLite storage type of the column.
    /// </summary>
    public string DeclaredType { get; }

    /// <summary>
    /// Gets the original source header name, when different from the SQL column name.
    /// </summary>
    public string SourceName { get; }
}
