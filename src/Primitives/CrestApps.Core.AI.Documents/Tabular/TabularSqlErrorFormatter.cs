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
    /// when the failure looks like a wrong table or column name.
    /// </summary>
    /// <param name="prefix">A short phrase describing what failed, for example "The query could not be executed".</param>
    /// <param name="exception">The SQLite exception raised while running the query.</param>
    /// <param name="tables">The tables loaded in the workspace at the time of the failure.</param>
    /// <returns>The error text to return from the tool.</returns>
    public static string Format(string prefix, SqliteException exception, IReadOnlyList<TabularTableInfo> tables)
    {
        var message = $"{prefix}: {exception.Message}";

        if (tables is not { Count: > 0 } || !IsSchemaError(exception.Message))
        {
            return message;
        }

        using var builder = ZString.CreateStringBuilder();

        builder.AppendLine(message);
        builder.AppendLine();
        builder.AppendLine("Available tables and columns:");

        foreach (var table in tables)
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
