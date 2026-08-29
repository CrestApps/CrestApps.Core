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

        // Wrap the identifier in double quotes (doubling any embedded quote) so names that are not valid
        // unquoted identifiers — e.g. a table derived from an index profile named "data-sources", whose
        // hyphen would otherwise raise "42601: syntax error at or near '-'" — are emitted correctly.
        var escapedName = trimmedName.Replace("\"", "\"\"", StringComparison.Ordinal);

        return $"\"{escapedName}\"";
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex SafeTableNameRegex();

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex SafeIdentifierRegex();

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex SafeColumnNameRegex();
}
