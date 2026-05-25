using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CrestApps.Core.PostgreSQL.Services;

/// <summary>
/// Creates and caches PostgreSQL data sources from the configured connection options.
/// </summary>
public sealed class PostgreSQLClientFactory : IPostgreSQLClientFactory, IAsyncDisposable
{
    private readonly ILogger<PostgreSQLClientFactory> _logger;
    private readonly PostgreSQLConnectionOptions _options;
    private readonly Lock _syncLock = new();

    private NpgsqlDataSource _dataSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLClientFactory"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="options">The connection options.</param>
    public PostgreSQLClientFactory(
        ILogger<PostgreSQLClientFactory> logger,
        IOptions<PostgreSQLConnectionOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Creates or returns the cached PostgreSQL data source using the default connection options.
    /// </summary>
    public NpgsqlDataSource Create()
    {
        if (_dataSource != null)
        {
            return _dataSource;
        }

        lock (_syncLock)
        {
            _dataSource ??= Create(_options);
        }

        return _dataSource;
    }

    /// <summary>
    /// Creates a PostgreSQL data source for the supplied configuration.
    /// </summary>
    /// <param name="configuration">The connection options to use.</param>
    public NpgsqlDataSource Create(PostgreSQLConnectionOptions configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.ConnectionString))
        {
            throw new InvalidOperationException("PostgreSQL is not configured. A connection string is required.");
        }

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(configuration.ConnectionString);
        dataSourceBuilder.UseVector();

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Creating PostgreSQL data source with the configured connection string.");
        }

        return dataSourceBuilder.Build();
    }

    /// <summary>
    /// Disposes the cached PostgreSQL data source when the application shuts down.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_dataSource is null)
        {
            return;
        }

        await _dataSource.DisposeAsync();
        _dataSource = null;
    }
}
