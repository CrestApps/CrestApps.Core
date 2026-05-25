using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Support;
using Npgsql;

namespace CrestApps.Core.PostgreSQL.Services;

/// <summary>
/// Reads documents from a PostgreSQL source index table.
/// </summary>
internal sealed class DataSourcePostgreSQLDocumentReader : IDataSourceDocumentReader
{
    private const int BatchSize = 1000;

    private readonly IPostgreSQLClientFactory _clientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSourcePostgreSQLDocumentReader"/> class.
    /// </summary>
    /// <param name="clientFactory">The PostgreSQL client factory.</param>
    public DataSourcePostgreSQLDocumentReader(IPostgreSQLClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    /// <summary>
    /// Reads all documents from the index table with cursor-based pagination.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="keyFieldName">The field name to use as the document key.</param>
    /// <param name="titleFieldName">The field name to use as the document title.</param>
    /// <param name="contentFieldName">The field name to use as the document content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(
        IIndexProfileInfo indexProfile,
        string keyFieldName,
        string titleFieldName,
        string contentFieldName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (indexProfile == null)
        {
            yield break;
        }

        var tableName = PostgreSQLSearchIndexManager.SanitizeTableName(indexProfile.IndexFullName);
        var quotedTableName = PostgreSQLHelpers.QuoteIdentifier(tableName);
        var offset = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var dataSource = _clientFactory.Create();
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            command.CommandText = $"""SELECT * FROM {quotedTableName} ORDER BY {PostgreSQLHelpers.SanitizeColumnName(keyFieldName)} LIMIT @limit OFFSET @offset""";
            command.Parameters.AddWithValue("limit", BatchSize);
            command.Parameters.AddWithValue("offset", offset);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rowCount = 0;

            while (await reader.ReadAsync(cancellationToken))
            {
                rowCount++;
                var row = ReadRow(reader);
                var key = ResolveKey(row, keyFieldName);

                yield return new KeyValuePair<string, SourceDocument>(
                    key, ExtractDocument(row, titleFieldName, contentFieldName));
            }

            if (rowCount < BatchSize)
            {
                break;
            }

            offset += BatchSize;
        }
    }

    /// <summary>
    /// Reads specific documents by their IDs from the index table.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="documentIds">The IDs of documents to read.</param>
    /// <param name="keyFieldName">The field name to use as the document key.</param>
    /// <param name="titleFieldName">The field name to use as the document title.</param>
    /// <param name="contentFieldName">The field name to use as the document content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadByIdsAsync(
        IIndexProfileInfo indexProfile,
        IEnumerable<string> documentIds,
        string keyFieldName,
        string titleFieldName,
        string contentFieldName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (indexProfile == null || documentIds == null)
        {
            yield break;
        }

        var idList = documentIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
        if (idList.Count == 0)
        {
            yield break;
        }

        var tableName = PostgreSQLSearchIndexManager.SanitizeTableName(indexProfile.IndexFullName);
        var quotedTableName = PostgreSQLHelpers.QuoteIdentifier(tableName);
        var dataSource = _clientFactory.Create();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var paramNames = new List<string>();
        for (var i = 0; i < idList.Count; i++)
        {
            var paramName = $"@id{i}";
            paramNames.Add(paramName);
            command.Parameters.AddWithValue(paramName, idList[i]);
        }

        command.CommandText = $"""SELECT * FROM {quotedTableName} WHERE {PostgreSQLHelpers.SanitizeColumnName(keyFieldName)} IN ({string.Join(", ", paramNames)})""";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = ReadRow(reader);
            var key = ResolveKey(row, keyFieldName);

            yield return new KeyValuePair<string, SourceDocument>(
                key, ExtractDocument(row, titleFieldName, contentFieldName));
        }
    }

    private static Dictionary<string, object> ReadRow(NpgsqlDataReader reader)
    {
        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
            row[name] = value;
        }

        return row;
    }

    private static string ResolveKey(Dictionary<string, object> row, string keyFieldName)
    {
        if (!string.IsNullOrEmpty(keyFieldName) && row.TryGetValue(keyFieldName, out var keyValue) && keyValue != null)
        {
            return keyValue.ToString();
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

        if (!string.IsNullOrEmpty(titleFieldName) && row.TryGetValue(titleFieldName, out var titleValue) && titleValue != null)
        {
            title = titleValue.ToString();
        }

        if (!string.IsNullOrEmpty(contentFieldName) && row.TryGetValue(contentFieldName, out var contentValue) && contentValue != null)
        {
            content = contentValue.ToString();
        }

        if (string.IsNullOrEmpty(content))
        {
            var jsonObj = new JsonObject();
            foreach (var kvp in row)
            {
                jsonObj[kvp.Key] = JsonValue.Create(kvp.Value?.ToString());
            }

            content = jsonObj.ToJsonString();
        }

        if (string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(content))
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
