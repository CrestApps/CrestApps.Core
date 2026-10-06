using System.Globalization;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Documents.Word.Workspace;
using Microsoft.Data.Sqlite;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// Reads rows for a table or a chart from the conversation's uploaded tabular data, so the values in a document
/// are the real ones rather than numbers a model remembered — and carry the number formats the spreadsheet
/// export and the tabular preview present them with.
/// </summary>
internal static class WordTabularSource
{
    private const int DefaultMaxRows = 200;
    private const int MaxRowsLimit = 2000;

    /// <summary>
    /// Runs a tabular source: a SQL query, or a whole loaded table by name.
    /// </summary>
    /// <param name="source">The source object: <c>sql</c> or <c>table_name</c>, and optionally <c>max_rows</c>.</param>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result.</returns>
    public static async Task<WordTabularResult> QueryAsync(JsonElement source, IServiceProvider services, CancellationToken cancellationToken)
    {
        var prepared = await TabularToolRunner.PrepareAsync(services, cancellationToken);

        try
        {
            if (prepared.Error is not null)
            {
                throw new WordToolException("This content reads uploaded tabular data, but none can be read: " + prepared.Error);
            }

            var maxRows = Math.Clamp(WordJsonValues.GetInt(source, "max_rows") ?? DefaultMaxRows, 1, MaxRowsLimit);
            var sqlText = WordJsonValues.GetString(source, "sql");
            var tableName = WordJsonValues.GetString(source, "table_name") ?? WordJsonValues.GetString(source, "table");
            TabularTableInfo table = null;
            string sql;

            if (!string.IsNullOrWhiteSpace(sqlText))
            {
                sql = sqlText.Trim().TrimEnd(';');
            }
            else if (!string.IsNullOrWhiteSpace(tableName))
            {
                var name = tableName.Trim();

                table = prepared.Tables.FirstOrDefault(candidate => string.Equals(candidate.TableName, name, StringComparison.OrdinalIgnoreCase))
                    ?? prepared.Tables.FirstOrDefault(candidate => string.Equals(candidate.WorksheetName, name, StringComparison.OrdinalIgnoreCase))
                    ?? prepared.Tables.FirstOrDefault(candidate => string.Equals(candidate.SourceFileName, name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new WordToolException($"There is no loaded table named \"{name}\". Loaded tables: {string.Join(", ", prepared.Tables.Select(candidate => candidate.TableName))}.");

                sql = "SELECT * FROM " + TabularWorkspaceSqliteHelpers.QuoteIdentifier(table.TableName);
            }
            else
            {
                throw new WordToolException("A tabular 'source' needs 'sql' (SQLite, table names from list_tabular_data) or 'table_name'.");
            }

            TabularQueryResult result;

            try
            {
                result = await prepared.Workspace.QueryAsync(sql, maxRows, cancellationToken);
            }
            catch (Exception ex) when (ex is TabularSqlException or SqliteException)
            {
                throw new WordToolException($"The tabular query failed: {ex.Message}", ex);
            }

            var headers = new List<string>(result.Columns.Count);

            foreach (var column in result.Columns)
            {
                var info = table?.Columns.FirstOrDefault(candidate => string.Equals(candidate.Name, column, StringComparison.OrdinalIgnoreCase));

                // A whole table is headed the way the uploaded file headed it, not by its SQL identifiers.
                headers.Add(string.IsNullOrWhiteSpace(info?.SourceName) ? column : info.SourceName);
            }

            var stored = table is null
                ? await prepared.Workspace.GetFormattingAsync(TabularToolNames.WorkspaceFormattingKey, cancellationToken)
                : await prepared.Workspace.GetFormattingAsync(table.TableName, cancellationToken);

            if (table is not null && string.IsNullOrEmpty(stored.SpecJson))
            {
                stored = await prepared.Workspace.GetFormattingAsync(TabularToolNames.WorkspaceFormattingKey, cancellationToken);
            }

            var formatting = TabularFormattingResolver.Resolve(stored.SpecJson, headers, table);
            var description = table is null
                ? "Query over the uploaded tabular data"
                : string.IsNullOrWhiteSpace(table.WorksheetName)
                    ? $"Table {table.TableName} from {table.SourceFileName}"
                    : $"Worksheet \"{table.WorksheetName}\" of {table.SourceFileName}";

            return new WordTabularResult(headers, result.Rows, formatting, result.Truncated, description);
        }
        finally
        {
            prepared.Workspace?.Dispose();
        }
    }

    /// <summary>
    /// Reads a cell value as a number for a chart.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The number, or <see langword="null"/>.</returns>
    public static double? ToNumber(object value)
    {
        return value switch
        {
            null or DBNull => null,
            double number => number,
            float number => number,
            decimal number => (double)number,
            long number => number,
            int number => number,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}

/// <summary>
/// The rows a tabular source returned.
/// </summary>
/// <param name="Headers">The column headers.</param>
/// <param name="Rows">The rows.</param>
/// <param name="Formatting">The column formats recorded for the data.</param>
/// <param name="Truncated">Whether more rows exist than were returned.</param>
/// <param name="Description">Where the rows came from.</param>
internal sealed record WordTabularResult(
    IReadOnlyList<string> Headers,
    IReadOnlyList<object[]> Rows,
    SpreadsheetFormatting Formatting,
    bool Truncated,
    string Description);
