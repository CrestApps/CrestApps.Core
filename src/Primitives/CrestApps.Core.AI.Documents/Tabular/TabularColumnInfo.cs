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
    public TabularColumnInfo(string name, string declaredType, string sourceName = null, string sourceFormat = null)
    {
        Name = name;
        DeclaredType = declaredType;
        SourceName = sourceName;
        SourceFormat = sourceFormat;
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

    /// <summary>
    /// Gets the number format code the column used in the source file, when the source carried one.
    /// <para>
    /// This is what lets an export reproduce the presentation the upload had — a column that was
    /// currency or a percentage comes back that way without the caller having to ask. It is a default
    /// only; formatting the caller requests explicitly always wins.
    /// </para>
    /// </summary>
    public string SourceFormat { get; }
}
