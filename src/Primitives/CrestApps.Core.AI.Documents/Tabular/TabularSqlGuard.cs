using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Validates SQL submitted by the model before it runs against an in-memory tabular workspace.
/// Queries are restricted to a single read-only statement, and manipulation statements are
/// limited to data/schema operations that cannot reach the host file system.
/// </summary>
internal static partial class TabularSqlGuard
{
    private static readonly string[] _readOnlyPrefixes = ["SELECT", "WITH"];

    private static readonly string[] _commandPrefixes =
        ["SELECT", "WITH", "INSERT", "UPDATE", "DELETE", "REPLACE", "ALTER", "CREATE", "DROP"];

    /// <summary>
    /// Ensures the SQL is a single read-only query, throwing when it is not.
    /// </summary>
    /// <param name="sql">The SQL to validate.</param>
    /// <returns>The normalized single-statement SQL.</returns>
    public static string EnsureReadOnlyQuery(string sql)
    {
        var statement = Normalize(sql);

        if (!StartsWithAny(statement, _readOnlyPrefixes))
        {
            throw new TabularSqlException("Only read-only queries that start with SELECT or WITH are allowed here. Use the manipulation tool to change data.");
        }

        return statement;
    }

    /// <summary>
    /// Ensures the SQL is a single allowed data/schema statement, throwing when it is not.
    /// </summary>
    /// <param name="sql">The SQL to validate.</param>
    /// <returns>The normalized single-statement SQL.</returns>
    public static string EnsureCommand(string sql)
    {
        var statement = Normalize(sql);

        if (!StartsWithAny(statement, _commandPrefixes))
        {
            throw new TabularSqlException("The statement must be a single SELECT, WITH, INSERT, UPDATE, DELETE, REPLACE, ALTER, CREATE, or DROP statement.");
        }

        return statement;
    }

    private static string Normalize(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new TabularSqlException("No SQL statement was provided.");
        }

        // Allow a single trailing semicolon but reject statement batching.
        var statement = sql.Trim().TrimEnd(';').Trim();

        if (statement.Contains(';', StringComparison.Ordinal))
        {
            throw new TabularSqlException("Only a single SQL statement is allowed per call.");
        }

        // Reject keywords that could reach the file system, load native code, or escape the
        // in-memory sandbox, regardless of statement type.
        var forbidden = ForbiddenKeywordsRegex().Match(statement);

        if (forbidden.Success)
        {
            throw new TabularSqlException($"The '{forbidden.Value}' keyword is not permitted.");
        }

        return statement;
    }

    private static bool StartsWithAny(string statement, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (statement.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                (statement.Length == prefix.Length || !char.IsLetterOrDigit(statement[prefix.Length])))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"\b(ATTACH|DETACH|PRAGMA|VACUUM|load_extension)\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ForbiddenKeywordsRegex();
}
