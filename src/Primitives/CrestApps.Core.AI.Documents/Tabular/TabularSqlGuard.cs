using System.Text;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Validates SQL submitted by the model before it runs against an in-memory tabular workspace.
/// Read-only queries are restricted to a single statement, while manipulation calls may submit one
/// or more data/schema statements in a single batch. Every statement is limited to data/schema
/// operations that cannot reach the host file system.
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

    /// <summary>
    /// Ensures the SQL contains one or more allowed data/schema statements, throwing when any
    /// statement is not allowed. Multiple statements may be supplied in a single call so the model
    /// can apply all changes in one batch instead of many round-trips.
    /// </summary>
    /// <param name="sql">The SQL batch to validate. Statements are separated by semicolons.</param>
    /// <returns>The normalized, individually validated statements in submission order.</returns>
    public static IReadOnlyList<string> EnsureCommandBatch(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new TabularSqlException("No SQL statement was provided.");
        }

        var statements = SplitStatements(sql);

        if (statements.Count == 0)
        {
            throw new TabularSqlException("No SQL statement was provided.");
        }

        foreach (var statement in statements)
        {
            EnsureNoForbiddenKeyword(statement);

            if (!StartsWithAny(statement, _commandPrefixes))
            {
                throw new TabularSqlException("Each statement must be a SELECT, WITH, INSERT, UPDATE, DELETE, REPLACE, ALTER, CREATE, or DROP statement.");
            }
        }

        return statements;
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

        EnsureNoForbiddenKeyword(statement);

        return statement;
    }

    private static void EnsureNoForbiddenKeyword(string statement)
    {
        // Reject keywords that could reach the file system, load native code, or escape the
        // in-memory sandbox, regardless of statement type.
        var forbidden = ForbiddenKeywordsRegex().Match(statement);

        if (forbidden.Success)
        {
            throw new TabularSqlException($"The '{forbidden.Value}' keyword is not permitted.");
        }
    }

    /// <summary>
    /// Splits a SQL batch into individual statements, respecting single-quoted string literals,
    /// double-quoted identifiers, and SQL line/block comments so that semicolons inside those
    /// constructs do not split a statement.
    /// </summary>
    /// <param name="sql">The SQL batch to split.</param>
    /// <returns>The non-empty trimmed statements in submission order.</returns>
    private static List<string> SplitStatements(string sql)
    {
        var statements = new List<string>();
        var builder = new StringBuilder(sql.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var current = sql[i];

            if (inSingleQuote)
            {
                builder.Append(current);

                if (current == '\'')
                {
                    // A doubled single quote ('') is an escaped quote, not the end of the literal.
                    if (i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        builder.Append(sql[++i]);
                    }
                    else
                    {
                        inSingleQuote = false;
                    }
                }

                continue;
            }

            if (inDoubleQuote)
            {
                builder.Append(current);

                if (current == '"')
                {
                    // A doubled double quote ("") is an escaped quote inside an identifier.
                    if (i + 1 < sql.Length && sql[i + 1] == '"')
                    {
                        builder.Append(sql[++i]);
                    }
                    else
                    {
                        inDoubleQuote = false;
                    }
                }

                continue;
            }

            // Skip line comments (-- ... end of line).
            if (current == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                i += 2;

                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            // Skip block comments (/* ... */).
            if (current == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;

                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                {
                    i++;
                }

                i++;

                continue;
            }

            if (current == '\'')
            {
                inSingleQuote = true;
                builder.Append(current);

                continue;
            }

            if (current == '"')
            {
                inDoubleQuote = true;
                builder.Append(current);

                continue;
            }

            if (current == ';')
            {
                AppendStatement(statements, builder);

                continue;
            }

            builder.Append(current);
        }

        AppendStatement(statements, builder);

        return statements;
    }

    private static void AppendStatement(List<string> statements, StringBuilder builder)
    {
        var statement = builder.ToString().Trim();

        builder.Clear();

        if (statement.Length > 0)
        {
            statements.Add(statement);
        }
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
