using System.Text.RegularExpressions;

namespace CrestApps.Core.PostgreSQL;

/// <summary>
/// Provides shared helper methods for PostgreSQL identifier sanitization.
/// </summary>
public static partial class PostgreSQLHelpers
{
    /// <summary>
    /// Validates and normalizes a table name for quoted PostgreSQL usage.
    /// </summary>
    /// <param name="name">The raw table name.</param>
    public static string SanitizeTableName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmedName = name.Trim();
        if (!SafeTableNameRegex().IsMatch(trimmedName))
        {
            throw new InvalidOperationException($"The PostgreSQL table name '{trimmedName}' contains unsupported characters.");
        }

        return trimmedName.ToLowerInvariant();
    }

    /// <summary>
    /// Validates and normalizes a name for use as an unquoted PostgreSQL identifier.
    /// </summary>
    /// <param name="name">The raw identifier name.</param>
    public static string SanitizeIdentifier(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmedName = name.Trim();
        if (!SafeIdentifierRegex().IsMatch(trimmedName))
        {
            throw new InvalidOperationException($"The PostgreSQL identifier '{trimmedName}' contains unsupported characters.");
        }

        return trimmedName
            .Replace("-", "_", StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    /// <summary>
    /// Validates and double-quotes a column name for use in SQL statements.
    /// </summary>
    /// <param name="name">The raw column name.</param>
    public static string SanitizeColumnName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmedName = name.Trim();
        if (!SafeColumnNameRegex().IsMatch(trimmedName))
        {
            throw new InvalidOperationException($"The PostgreSQL column name '{trimmedName}' contains unsupported characters.");
        }

        return QuoteIdentifier(trimmedName);
    }

    /// <summary>
    /// Quotes a validated PostgreSQL identifier for use in SQL statements.
    /// </summary>
    /// <param name="name">The identifier name.</param>
    public static string QuoteIdentifier(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmedName = name.Trim();
        if (!SafeIdentifierRegex().IsMatch(trimmedName))
        {
            throw new InvalidOperationException($"The PostgreSQL identifier '{trimmedName}' contains unsupported characters.");
        }

        return $""""{trimmedName.Replace("\"", "\"\"", StringComparison.Ordinal)}"""";
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex SafeTableNameRegex();

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex SafeIdentifierRegex();

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex SafeColumnNameRegex();
}
