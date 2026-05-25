using System.Text.RegularExpressions;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;

namespace CrestApps.Core.PostgreSQL.Services;

/// <summary>
/// Translates OData filter expressions into PostgreSQL WHERE clause fragments
/// targeting the "filters" JSONB column in the knowledge base index table.
/// Supports: eq, ne, gt, lt, ge, le, and, or, not, contains, startswith, endswith.
/// </summary>
internal sealed partial class PostgreSQLODataFilterTranslator : IODataFilterTranslator
{
    /// <summary>
    /// Translates an OData filter expression to a PostgreSQL WHERE clause fragment.
    /// </summary>
    /// <param name="odataFilter">The OData filter expression to translate.</param>
    public string Translate(string odataFilter)
    {
        if (string.IsNullOrWhiteSpace(odataFilter))
        {
            return null;
        }

        var tokens = Tokenize(odataFilter);
        if (tokens.Count == 0)
        {
            return null;
        }

        var index = 0;
        var result = ParseExpression(tokens, ref index);

        return result;
    }

    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var regex = TokenRegex();

        foreach (Match match in regex.Matches(input))
        {
            tokens.Add(match.Value);
        }

        return tokens;
    }

    private static string ParseExpression(List<string> tokens, ref int index)
    {
        var left = ParseUnary(tokens, ref index);

        while (index < tokens.Count)
        {
            var token = tokens[index];

            if (string.Equals(token, "and", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                var right = ParseUnary(tokens, ref index);
                left = $"({left} AND {right})";
            }
            else if (string.Equals(token, "or", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                var right = ParseUnary(tokens, ref index);
                left = $"({left} OR {right})";
            }
            else
            {
                break;
            }
        }

        return left;
    }

    private static string ParseUnary(List<string> tokens, ref int index)
    {
        if (index < tokens.Count && string.Equals(tokens[index], "not", StringComparison.OrdinalIgnoreCase))
        {
            index++;
            var operand = ParsePrimary(tokens, ref index);

            return $"NOT ({operand})";
        }

        return ParsePrimary(tokens, ref index);
    }

    private static string ParsePrimary(List<string> tokens, ref int index)
    {
        if (index >= tokens.Count)
        {
            return "TRUE";
        }

        if (tokens[index] == "(")
        {
            index++;
            var result = ParseExpression(tokens, ref index);

            if (index < tokens.Count && tokens[index] == ")")
            {
                index++;
            }

            return $"({result})";
        }

        // Function-style: contains(field, 'value'), startswith(...), endswith(...)
        if (index + 1 < tokens.Count && tokens[index + 1] == "(")
        {
            var funcName = tokens[index].ToLowerInvariant();
            index += 2;
            var field = PrefixField(tokens[index]);
            index++;

            if (index < tokens.Count && tokens[index] == ",")
            {
                index++;
            }

            var value = UnquoteValue(tokens[index]);
            index++;

            if (index < tokens.Count && tokens[index] == ")")
            {
                index++;
            }

            return funcName switch
            {
                "contains" => $"{field} ILIKE '%{EscapeLike(value)}%'",
                "startswith" => $"{field} ILIKE '{EscapeLike(value)}%'",
                "endswith" => $"{field} ILIKE '%{EscapeLike(value)}'",
                _ => "TRUE",
            };
        }

        // Binary comparison: field op value
        var fieldToken = tokens[index];
        index++;

        if (index >= tokens.Count)
        {
            return "TRUE";
        }

        var op = tokens[index].ToLowerInvariant();
        index++;

        if (index >= tokens.Count)
        {
            return "TRUE";
        }

        var valueToken = tokens[index];
        index++;

        var prefixedField = PrefixField(fieldToken);
        var parsedValue = UnquoteValue(valueToken);

        return op switch
        {
            "eq" => $"{prefixedField} = '{EscapeSql(parsedValue)}'",
            "ne" => $"{prefixedField} <> '{EscapeSql(parsedValue)}'",
            "gt" => $"{prefixedField} > '{EscapeSql(parsedValue)}'",
            "ge" => $"{prefixedField} >= '{EscapeSql(parsedValue)}'",
            "lt" => $"{prefixedField} < '{EscapeSql(parsedValue)}'",
            "le" => $"{prefixedField} <= '{EscapeSql(parsedValue)}'",
            _ => "TRUE",
        };
    }

    private static string PrefixField(string field)
    {
        if (field.StartsWith($"{DataSourceConstants.ColumnNames.Filters}.", StringComparison.OrdinalIgnoreCase))
        {
            var jsonPath = field[(DataSourceConstants.ColumnNames.Filters.Length + 1)..]
                .Replace(".", ",", StringComparison.Ordinal);

            return $"\"{DataSourceConstants.ColumnNames.Filters}\"#>>'{{{EscapeSql(jsonPath)}}}'";
        }

        return $"\"{DataSourceConstants.ColumnNames.Filters}\"#>>'{{{EscapeSql(field.Replace(".", ",", StringComparison.Ordinal))}}}'";
    }

    private static string UnquoteValue(string value)
    {
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1];
        }

        return value;
    }

    private static string EscapeSql(string value)
    {
        return value.Replace("'", "''");
    }

    private static string EscapeLike(string value)
    {
        return EscapeSql(value)
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    [GeneratedRegex(@"'[^']*'|[(),]|\w[\w.]*")]
    private static partial Regex TokenRegex();
}
