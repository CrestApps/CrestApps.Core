using Npgsql;

namespace CrestApps.Core.PostgreSQL.Services;

/// <summary>
/// Creates PostgreSQL data sources from the configured connection options.
/// </summary>
public interface IPostgreSQLClientFactory
{
    /// <summary>
    /// Creates the configured PostgreSQL data source using the default connection options.
    /// The returned instance is cached and owned by the factory.
    /// </summary>
    NpgsqlDataSource Create();

    /// <summary>
    /// Creates a PostgreSQL data source for the supplied configuration.
    /// </summary>
    /// <param name="configuration">The connection options to use.</param>
    NpgsqlDataSource Create(PostgreSQLConnectionOptions configuration);
}
