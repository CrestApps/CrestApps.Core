namespace CrestApps.Core.PostgreSQL;

/// <summary>
/// Provides shared helper methods for PostgreSQL identifier sanitization.
/// </summary>
public static class PostgreSQLHelpers
{
    /// <summary>
    /// Sanitizes a table name by removing dangerous characters and lowercasing.
    /// </summary>
    /// <param name="name">The raw table name.</param>
    public static string SanitizeTableName(string name)
    {
        return name?.Replace("\"", "").Replace("'", "").Replace(";", "").ToLowerInvariant();
    }

    /// <summary>
    /// Sanitizes a name for use as an unquoted PostgreSQL identifier (e.g., index names)
    /// by replacing characters that are invalid in unquoted identifiers with underscores.
    /// </summary>
    /// <param name="name">The raw identifier name.</param>
    public static string SanitizeIdentifier(string name)
    {
        return name?.Replace("\"", "").Replace("'", "").Replace(";", "").Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
    }

    /// <summary>
    /// Sanitizes and double-quotes a column name for use in SQL statements.
    /// </summary>
    /// <param name="name">The raw column name.</param>
    public static string SanitizeColumnName(string name)
    {
        return $"\"{name?.Replace("\"", "").Replace("'", "").Replace(";", "")}\"";
    }
}
