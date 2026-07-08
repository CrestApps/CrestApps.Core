namespace CrestApps.Core.AI.Models;

/// <summary>
/// Stores explicit PostgreSQL source connection settings for an AI data source.
/// </summary>
public sealed class PostgreSQLSourceMetadata
{
    /// <summary>
    /// Gets or sets the protected PostgreSQL connection string.
    /// </summary>
    public string ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the table name to read from.
    /// </summary>
    public string TableName { get; set; }
}
