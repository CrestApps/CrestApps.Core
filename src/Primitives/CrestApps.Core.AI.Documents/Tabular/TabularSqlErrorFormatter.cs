using Cysharp.Text;
using Microsoft.Data.Sqlite;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Enriches a failed tabular SQL query with the actual table and column names available in the
/// workspace, so the model has something concrete to correct its next attempt with instead of a bare
/// SQLite message.
/// </summary>
internal static class TabularSqlErrorFormatter
{
    /// <summary>
    /// Builds the error text returned to the model for a failed query, appending the available schema
    /// when the failure looks like a wrong table or column name. When <paramref name="sql"/> is given
    /// and names at least one real table, only that table's columns are listed — a column-name mismatch
    /// almost always means the table itself was right, so dumping every other loaded table's columns
    /// alongside it is noise that buries the one correction that matters. A query that named no real
    /// table at all (for example a guessed table name) falls back to listing everything, since in that
    /// case the model needs to see the real table names, not just one guessed column's siblings.
    /// </summary>
    /// <param name="prefix">A short phrase describing what failed, for example "The query could not be executed".</param>
    /// <param name="exception">The SQLite exception raised while running the query.</param>
    /// <param name="tables">The tables loaded in the workspace at the time of the failure.</param>
    /// <param name="sql">The SQL that failed, used to narrow the schema dump to the table(s) it names.</param>
    /// <returns>The error text to return from the tool.</returns>
    public static string Format(string prefix, SqliteException exception, IReadOnlyList<TabularTableInfo> tables, string sql = null)
    {
        var message = $"{prefix}: {exception.Message}";

        if (tables is not { Count: > 0 } || !IsSchemaError(exception.Message))
        {
            return message;
        }

        var referencedTables = string.IsNullOrWhiteSpace(sql) ? [] : TabularResultAnalyzer.FindReferencedTables(sql, tables);
        var tablesToShow = referencedTables.Count > 0 ? referencedTables : tables;

        using var builder = ZString.CreateStringBuilder();

        builder.AppendLine(message);
        builder.AppendLine();
        builder.AppendLine("Available tables and columns:");

        foreach (var table in tablesToShow)
        {
            builder.Append("Table \"");
            builder.Append(table.TableName);
            builder.Append("\": ");
            builder.AppendLine(string.Join(", ", table.Columns.Select(FormatColumn)));
        }

        return builder.ToString();
    }

    private static bool IsSchemaError(string message)
    {
        return message.Contains("no such column", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("no such table", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatColumn(TabularColumnInfo column)
    {
        if (string.IsNullOrWhiteSpace(column.SourceName) ||
            string.Equals(column.Name, column.SourceName, StringComparison.OrdinalIgnoreCase))
        {
            return column.Name;
        }

        return $"{column.Name} (source header: {column.SourceName})";
    }
}
