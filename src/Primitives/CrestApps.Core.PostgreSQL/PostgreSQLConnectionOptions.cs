namespace CrestApps.Core.PostgreSQL;

/// <summary>
/// Options for configuring a PostgreSQL connection used by the indexing provider.
/// Bind from configuration (e.g. "CrestApps:PostgreSQL").
/// </summary>
public sealed class PostgreSQLConnectionOptions
{
    /// <summary>
    /// Gets or sets the PostgreSQL connection string.
    /// </summary>
    public string ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets an optional prefix applied to managed index table names.
    /// </summary>
    public string IndexPrefix { get; set; }
}
