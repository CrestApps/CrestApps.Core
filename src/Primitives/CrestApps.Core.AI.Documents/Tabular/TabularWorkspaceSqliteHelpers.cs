using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Shared SQLite table-shaping helpers used by tabular workspace importers.
/// </summary>
public static class TabularWorkspaceSqliteHelpers
{
    /// <summary>
    /// The number of data rows sampled when inferring column storage types. Streaming importers buffer
    /// this many rows before creating their table so both import paths infer from the same evidence.
    /// </summary>
    public const int TypeSampleRowCount = 32;

    /// <summary>
    /// Builds the SQLite column definitions for a tabular header row.
    /// </summary>
    /// <param name="header">The source header row.</param>
    /// <returns>The normalized workspace columns.</returns>
    public static IReadOnlyList<TabularColumnInfo> BuildColumns(IReadOnlyList<string> header)
    {
        return BuildColumns(header, null);
    }

    /// <summary>
    /// Builds the SQLite column definitions for a tabular header row, deriving each column's storage
    /// type from the supplied sample rows. A column is only typed as numeric when every sampled value
    /// is numeric, so anything ambiguous keeps the <c>TEXT</c> storage used when no samples are given.
    /// </summary>
    /// <param name="header">The source header row.</param>
    /// <param name="sampleRows">The data rows to sample, or <see langword="null"/> to type every column as <c>TEXT</c>.</param>
    /// <returns>The normalized workspace columns.</returns>
    public static IReadOnlyList<TabularColumnInfo> BuildColumns(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> sampleRows)
    {
        ArgumentNullException.ThrowIfNull(header);

        var declaredTypes = InferDeclaredTypes(header.Count, sampleRows);
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

            columns.Add(new TabularColumnInfo(candidate, declaredTypes[i], sourceName));
        }

        return columns;
    }

    /// <summary>
    /// Determines whether a value should be bound as <see cref="DBNull"/> rather than as an empty
    /// string. Blank cells in a numeric column are stored as <c>NULL</c> so they neither participate in
    /// comparisons nor sort ahead of real numbers; <c>TEXT</c> columns keep their empty strings.
    /// </summary>
    /// <param name="declaredType">The column's declared SQLite storage type.</param>
    /// <param name="value">The cell value.</param>
    /// <returns><see langword="true"/> when the value should be stored as <c>NULL</c>.</returns>
    public static bool IsNullValue(string declaredType, string value)
    {
        return string.IsNullOrWhiteSpace(value) && !string.Equals(declaredType, "TEXT", StringComparison.Ordinal);
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
        var columnDefinitions = string.Join(", ", columns.Select(c => $"{QuoteIdentifier(c.Name)} {ResolveStorageType(c.DeclaredType)}"));
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

    // A declared type cannot be quoted the way an identifier can, so it is emitted verbatim into the
    // CREATE TABLE statement. Only the inferred storage types are allowed through.
    private static string ResolveStorageType(string declaredType)
    {
        return declaredType switch
        {
            "INTEGER" => "INTEGER",
            "REAL" => "REAL",
            _ => "TEXT",
        };
    }

    private static string[] InferDeclaredTypes(int columnCount, IEnumerable<IReadOnlyList<string>> sampleRows)
    {
        var declaredTypes = new string[columnCount];
        Array.Fill(declaredTypes, "TEXT");

        if (sampleRows is null)
        {
            return declaredTypes;
        }

        var isNumeric = new bool[columnCount];
        var isInteger = new bool[columnCount];
        var isText = new bool[columnCount];
        Array.Fill(isInteger, true);
        var sampledRowCount = 0;

        foreach (var row in sampleRows)
        {
            if (sampledRowCount >= TypeSampleRowCount)
            {
                break;
            }

            sampledRowCount++;

            for (var columnIndex = 0; columnIndex < columnCount && columnIndex < row.Count; columnIndex++)
            {
                var value = row[columnIndex];

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!TryClassifyNumeric(value.Trim(), out var valueIsInteger))
                {
                    isText[columnIndex] = true;

                    continue;
                }

                isNumeric[columnIndex] = true;
                isInteger[columnIndex] &= valueIsInteger;
            }
        }

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            if (!isNumeric[columnIndex] || isText[columnIndex])
            {
                continue;
            }

            declaredTypes[columnIndex] = isInteger[columnIndex] ? "INTEGER" : "REAL";
        }

        return declaredTypes;
    }

    private static bool TryClassifyNumeric(string value, out bool isInteger)
    {
        isInteger = false;

        // A leading zero followed by another digit marks an identifier such as a zip code or account
        // number. Storing those numerically would drop the zero and change the value.
        if (value.Length > 1 && value[0] == '0' && char.IsAsciiDigit(value[1]))
        {
            return false;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            isInteger = true;

            return true;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
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
