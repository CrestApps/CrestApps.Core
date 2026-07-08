using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.PostgreSQL;
using CrestApps.Core.PostgreSQL.Services;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CrestApps.Core.AI.PostgreSQL.Services;

internal sealed class PostgreSQLAIDataSourceSourceHandler : IAIDataSourceSourceHandler
{
    private readonly IPostgreSQLClientFactory _clientFactory;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<PostgreSQLAIDataSourceSourceHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLAIDataSourceSourceHandler"/> class.
    /// </summary>
    /// <param name="clientFactory">The client factory.</param>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    /// <param name="logger">The logger.</param>
    public PostgreSQLAIDataSourceSourceHandler(
        IPostgreSQLClientFactory clientFactory,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<PostgreSQLAIDataSourceSourceHandler> logger)
    {
        _clientFactory = clientFactory;
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets the source type.
    /// </summary>
    public string SourceType => AIDataSourceSourceTypes.PostgreSQL;

    /// <summary>
    /// Validates the operation.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="result">The result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask ValidateAsync(AIDataSource dataSource, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(result);

        if (!dataSource.TryGet<PostgreSQLSourceMetadata>(out var metadata))
        {
            result.Fail(new ValidationResult("PostgreSQL source settings are required.", [nameof(PostgreSQLSourceMetadata)]));

            return ValueTask.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(metadata.ConnectionString))
        {
            result.Fail(new ValidationResult("PostgreSQL connection string is required.", [nameof(PostgreSQLSourceMetadata.ConnectionString)]));
        }

        if (string.IsNullOrWhiteSpace(metadata.TableName))
        {
            result.Fail(new ValidationResult("PostgreSQL table name is required.", [nameof(PostgreSQLSourceMetadata.TableName)]));
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Gets reference type.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<string> GetReferenceTypeAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(SourceType);
    }

    /// <summary>
    /// Reads the operation.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(AIDataSource dataSource, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (source, metadata) = Resolve(dataSource);
        var tableName = PostgreSQLHelpers.SanitizeTableName(metadata.TableName);
        var quotedTableName = PostgreSQLHelpers.QuoteIdentifier(tableName);
        var offset = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            await using var connection = await source.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""SELECT * FROM {quotedTableName} ORDER BY {ResolveSortColumn(dataSource)} LIMIT @limit OFFSET @offset""";
            command.Parameters.AddWithValue("limit", 1000);
            command.Parameters.AddWithValue("offset", offset);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rowCount = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rowCount++;
                var row = ReadRow(reader);
                var key = ResolveKey(row, dataSource.KeyFieldName);

                yield return new KeyValuePair<string, SourceDocument>(key, ExtractDocument(row, dataSource.TitleFieldName, dataSource.ContentFieldName));
            }

            if (rowCount < 1000)
            {
                yield break;
            }

            offset += 1000;
        }
    }

    /// <summary>
    /// Reads by ids.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    /// <param name="documentIds">The document ids.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadByIdsAsync(AIDataSource dataSource, IEnumerable<string> documentIds, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ids = documentIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (ids.Length == 0)
        {
            yield break;
        }

        var (source, metadata) = Resolve(dataSource);
        var tableName = PostgreSQLHelpers.SanitizeTableName(metadata.TableName);
        var quotedTableName = PostgreSQLHelpers.QuoteIdentifier(tableName);
        var keyColumn = string.IsNullOrWhiteSpace(dataSource.KeyFieldName) ? PostgreSQLHelpers.SanitizeColumnName("id") : PostgreSQLHelpers.SanitizeColumnName(dataSource.KeyFieldName);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var parameterNames = new List<string>();
        for (var i = 0; i < ids.Length; i++)
        {
            var parameterName = $"@id{i}";
            parameterNames.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, ids[i]);
        }

        command.CommandText = $"""SELECT * FROM {quotedTableName} WHERE {keyColumn} IN ({string.Join(", ", parameterNames)})""";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = ReadRow(reader);
            var key = ResolveKey(row, dataSource.KeyFieldName);

            yield return new KeyValuePair<string, SourceDocument>(key, ExtractDocument(row, dataSource.TitleFieldName, dataSource.ContentFieldName));
        }
    }

    private (NpgsqlDataSource Source, PostgreSQLSourceMetadata Metadata) Resolve(AIDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        if (!dataSource.TryGet<PostgreSQLSourceMetadata>(out var metadata))
        {
            throw new InvalidOperationException("PostgreSQL source metadata is missing.");
        }

        var protector = _dataProtectionProvider.CreateProtector(AIDataSourceProtectionConstants.SourceSecretPurpose);
        var connectionString = DataProtectionHelper.Unprotect(protector, metadata.ConnectionString, _logger, "Failed to unprotect AI data source field '{FieldName}' for data source '{DataSourceId}'.", nameof(PostgreSQLSourceMetadata.ConnectionString), dataSource.ItemId);
        var dataSourceOptions = new PostgreSQLConnectionOptions
        {
            ConnectionString = connectionString,
        };

        return (_clientFactory.Create(dataSourceOptions), metadata);
    }

    private static string ResolveSortColumn(AIDataSource dataSource)
    {
        return string.IsNullOrWhiteSpace(dataSource.KeyFieldName)
            ? PostgreSQLHelpers.SanitizeColumnName("id")
            : PostgreSQLHelpers.SanitizeColumnName(dataSource.KeyFieldName);
    }

    private static Dictionary<string, object> ReadRow(NpgsqlDataReader reader)
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return row;
    }

    private static string ResolveKey(Dictionary<string, object> row, string keyFieldName)
    {
        if (!string.IsNullOrWhiteSpace(keyFieldName) && row.TryGetValue(keyFieldName, out var value) && value != null)
        {
            return value.ToString();
        }

        if (row.TryGetValue("id", out var idValue) && idValue != null)
        {
            return idValue.ToString();
        }

        return null;
    }

    private static SourceDocument ExtractDocument(Dictionary<string, object> row, string titleFieldName, string contentFieldName)
    {
        string title = null;
        string content = null;

        if (!string.IsNullOrWhiteSpace(titleFieldName) && row.TryGetValue(titleFieldName, out var titleValue) && titleValue != null)
        {
            title = titleValue.ToString();
        }

        if (!string.IsNullOrWhiteSpace(contentFieldName) && row.TryGetValue(contentFieldName, out var contentValue) && contentValue != null)
        {
            content = contentValue.ToString();
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            var json = new System.Text.Json.Nodes.JsonObject();
            foreach (var kvp in row)
            {
                json[kvp.Key] = System.Text.Json.Nodes.JsonValue.Create(kvp.Value?.ToString());
            }

            content = json.ToJsonString();
        }

        if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(content))
        {
            title = content.ExtractTitleFromContent();
        }

        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in row)
        {
            fields[kvp.Key] = kvp.Value;
        }

        return new SourceDocument
        {
            Title = title,
            Content = content,
            Fields = fields,
        };
    }
}
