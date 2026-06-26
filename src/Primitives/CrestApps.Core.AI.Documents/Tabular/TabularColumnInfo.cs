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
    public TabularColumnInfo(string name, string declaredType)
    {
        Name = name;
        DeclaredType = declaredType;
    }

    /// <summary>
    /// Gets the column name as used in SQL.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the declared SQLite storage type of the column.
    /// </summary>
    public string DeclaredType { get; }
}
