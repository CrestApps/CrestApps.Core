using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CrestApps.Core.PostgreSQL.Services;

/// <summary>
/// PostgreSQL implementation of <see cref="ISearchIndexManager"/>
/// for creating, deleting, and checking search indexes using PostgreSQL tables with pgvector support.
/// </summary>
internal sealed class PostgreSQLSearchIndexManager : ISearchIndexManager
{
    private readonly IPostgreSQLClientFactory _clientFactory;
    private readonly PostgreSQLConnectionOptions _options;
    private readonly ILogger<PostgreSQLSearchIndexManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLSearchIndexManager"/> class.
    /// </summary>
    /// <param name="clientFactory">The PostgreSQL client factory.</param>
    /// <param name="options">The connection options.</param>
    /// <param name="logger">The logger.</param>
    public PostgreSQLSearchIndexManager(
        IPostgreSQLClientFactory clientFactory,
        IOptions<PostgreSQLConnectionOptions> options,
        ILogger<PostgreSQLSearchIndexManager> logger)
    {
        _clientFactory = clientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Composes the full index name by combining the configured prefix with the profile's index name.
    /// </summary>
    /// <param name="profile">The index profile.</param>
    public string ComposeIndexFullName(IIndexProfileInfo profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var normalizedIndexName = profile.IndexName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedIndexName))
        {
            return normalizedIndexName;
        }

        return string.IsNullOrWhiteSpace(_options.IndexPrefix)
            ? normalizedIndexName
            : string.Concat(_options.IndexPrefix.Trim(), normalizedIndexName);
    }

    /// <summary>
    /// Checks whether the index table exists in the PostgreSQL database.
    /// </summary>
    /// <param name="profile">The index profile.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<bool> ExistsAsync(IIndexProfileInfo profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var indexFullName = profile.IndexFullName ?? ComposeIndexFullName(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexFullName);

        try
        {
            var dataSource = _clientFactory.Create();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            command.CommandText = "SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_name = @tableName)";
            command.Parameters.AddWithValue("tableName", SanitizeTableName(indexFullName));

            var result = await command.ExecuteScalarAsync(cancellationToken);

            return result is true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking existence of PostgreSQL index table '{IndexName}'.", indexFullName.SanitizeForLog());
            throw;
        }
    }

    /// <summary>
    /// Creates a new index table in PostgreSQL with the specified fields including pgvector columns.
    /// </summary>
    /// <param name="profile">The index profile.</param>
    /// <param name="fields">The field definitions for the index.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task CreateAsync(IIndexProfileInfo profile, IReadOnlyCollection<SearchIndexField> fields, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(fields);

        var tableName = SanitizeTableName(profile.IndexFullName);
        var quotedTableName = PostgreSQLHelpers.QuoteIdentifier(tableName);

        try
        {
            var dataSource = _clientFactory.Create();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            await EnsurePgVectorExtensionAsync(connection, cancellationToken);

            var columnDefinitions = new List<string>();
            var indexStatements = new List<string>();

            foreach (var field in fields)
            {
                var columnName = SanitizeColumnName(field.Name);
                var columnDef = field.FieldType switch
                {
                    SearchFieldType.Vector => $"{columnName} vector({field.VectorDimensions ?? 1536})",
                    SearchFieldType.Text => $"{columnName} TEXT",
                    SearchFieldType.Integer => $"{columnName} INTEGER",
                    SearchFieldType.Float => $"{columnName} REAL",
                    SearchFieldType.DateTime => $"{columnName} TIMESTAMPTZ",
                    _ => $"{columnName} TEXT",
                };

                if (field.IsKey)
                {
                    columnDef += " PRIMARY KEY";
                }

                columnDefinitions.Add(columnDef);

                var safeIndexPrefix = PostgreSQLHelpers.SanitizeIdentifier(tableName);
                var safeColumnName = PostgreSQLHelpers.SanitizeIdentifier(field.Name);

                if (field.FieldType == SearchFieldType.Vector)
                {
                    indexStatements.Add(
                        $"""CREATE INDEX IF NOT EXISTS ix_{safeIndexPrefix}_{safeColumnName}_vector ON {quotedTableName} USING ivfflat ({columnName} vector_cosine_ops) WITH (lists = 100)""");
                }
                else if (field.IsFilterable && field.FieldType != SearchFieldType.Text)
                {
                    indexStatements.Add(
                    $"""CREATE INDEX IF NOT EXISTS ix_{safeIndexPrefix}_{safeColumnName} ON {quotedTableName} ({columnName})""");
                }
            }

            var createTableSql = $"""CREATE TABLE IF NOT EXISTS {quotedTableName} ({string.Join(", ", columnDefinitions)})""";

            await using var command = connection.CreateCommand();
            command.CommandText = createTableSql;
            await command.ExecuteNonQueryAsync(cancellationToken);

            foreach (var indexSql in indexStatements)
            {
                await using var indexCommand = connection.CreateCommand();
                indexCommand.CommandText = indexSql;

                try
                {
                    await indexCommand.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (PostgresException ex)
                {
                    // Index creation failures are non-fatal since the table was already created.
                    // IVFFlat indexes may fail on empty tables, and other index issues can be resolved later.
                    _logger.LogWarning(ex, "Could not create index for table '{TableName}'. The index can be created later when data is available.", tableName.SanitizeForLog());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating PostgreSQL index table '{IndexName}'.", tableName.SanitizeForLog());
            throw;
        }
    }

    /// <summary>
    /// Deletes the index table from the PostgreSQL database.
    /// </summary>
    /// <param name="profile">The index profile.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task DeleteAsync(IIndexProfileInfo profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var indexFullName = !string.IsNullOrWhiteSpace(profile.IndexFullName) ? profile.IndexFullName : ComposeIndexFullName(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexFullName);

        var tableName = SanitizeTableName(indexFullName);

        try
        {
            var dataSource = _clientFactory.Create();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            command.CommandText = $"""DROP TABLE IF EXISTS {PostgreSQLHelpers.QuoteIdentifier(tableName)}""";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting PostgreSQL index table '{IndexName}'.", tableName.SanitizeForLog());
            throw;
        }
    }

    private static async Task EnsurePgVectorExtensionAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static string SanitizeTableName(string name)
    {
        return PostgreSQLHelpers.SanitizeTableName(name);
    }

    internal static string SanitizeColumnName(string name)
    {
        return PostgreSQLHelpers.SanitizeColumnName(name);
    }
}
