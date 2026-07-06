using System.Text;
using Microsoft.Data.Sqlite;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Shared SQLite table-shaping helpers used by tabular workspace importers.
/// </summary>
public static class TabularWorkspaceSqliteHelpers
{
    /// <summary>
    /// Builds the SQLite column definitions for a tabular header row.
    /// </summary>
    /// <param name="header">The source header row.</param>
    /// <returns>The normalized workspace columns.</returns>
    public static IReadOnlyList<TabularColumnInfo> BuildColumns(IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var columns = new List<TabularColumnInfo>(header.Count);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < header.Count; i++)
        {
            var sourceName = header[i];
            var name = SanitizeIdentifier(GetPreferredHeaderName(sourceName), $"column_{i + 1}");
            var candidate = name;
            var suffix = 2;

            while (!used.Add(candidate))
            {
                candidate = $"{name}_{suffix}";
                suffix++;
            }

            columns.Add(new TabularColumnInfo(candidate, "TEXT", sourceName));
        }

        return columns;
    }

    /// <summary>
    /// Creates a SQLite table using the supplied normalized columns.
    /// </summary>
    /// <param name="connection">The SQLite connection.</param>
    /// <param name="tableName">The destination table name.</param>
    /// <param name="columns">The normalized columns.</param>
    public static void CreateTable(
        SqliteConnection connection,
        string tableName,
        IReadOnlyList<TabularColumnInfo> columns)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrEmpty(tableName);
        ArgumentNullException.ThrowIfNull(columns);

        using var createCommand = connection.CreateCommand();
        var columnDefinitions = string.Join(", ", columns.Select(c => $"{QuoteIdentifier(c.Name)} TEXT"));
        createCommand.CommandText = $"CREATE TABLE {QuoteIdentifier(tableName)} ({columnDefinitions})";
        createCommand.ExecuteNonQuery();
    }

    /// <summary>
    /// Creates an empty placeholder table for a document with no header row.
    /// </summary>
    /// <param name="connection">The SQLite connection.</param>
    /// <param name="tableName">The destination table name.</param>
    public static void CreateEmptyPlaceholderTable(SqliteConnection connection, string tableName)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrEmpty(tableName);

        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE {QuoteIdentifier(tableName)} (\"value\" TEXT)";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Quotes a SQLite identifier safely.
    /// </summary>
    /// <param name="identifier">The unquoted identifier.</param>
    /// <returns>The quoted identifier.</returns>
    public static string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string GetPreferredHeaderName(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return header;
        }

        var trimmed = header.Trim();
        var slashIndex = trimmed.IndexOf('/');

        if (slashIndex > 0)
        {
            var prefix = trimmed[..slashIndex].Trim();

            if (IsCompactHeaderCode(prefix))
            {
                return prefix;
            }
        }

        return trimmed;
    }

    private static bool IsCompactHeaderCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            return false;
        }

        return value.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    private static string SanitizeIdentifier(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var c in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        var sanitized = builder.ToString().Trim('_');

        if (string.IsNullOrEmpty(sanitized))
        {
            return fallback;
        }

        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }
}
