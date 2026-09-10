using System.Globalization;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.Support;
using Cysharp.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// Compares an aggregated measure between two tabular queries by joining them on a shared key in
/// code. Assembling such a comparison from two separate result sets by hand produces a plausible
/// looking table even when the two key columns describe different things (locations against company
/// names, for example), because the non-matching keys simply appear with a zero on the other side.
/// Joining here instead makes that case detectable: when nothing matches, the keys from each side are
/// reported and no table is produced.
/// </summary>
public sealed class CompareTabularDataTool : AIFunction
{
    public const string TheName = TabularToolNames.CompareTabularData;

    internal const int MaxComparedRows = 200;
    internal const int MaxListedKeys = 25;
    internal const int SampleKeyCount = 8;

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "left_sql": {
          "type": "string",
          "description": "A read-only SQL query for the first side returning EXACTLY two columns: the key to match on (for example a client, product, or department name) and the numeric measure to compare. Aggregate it yourself, for example: SELECT \"Campaign\", SUM(\"Total_Revenue\") FROM \"Client_Breakdown\" WHERE is_subtotal = 0 GROUP BY \"Campaign\". Use a CASE expression on the key column when variants of the same name should be combined."
        },
        "right_sql": {
          "type": "string",
          "description": "A read-only SQL query for the second side, in the same shape as left_sql: exactly two columns, the matching key first and the numeric measure second. This normally reads a different table or a different uploaded file."
        },
        "left_label": {
          "type": "string",
          "description": "Optional short name for the first side, used as its column header (for example the file or worksheet it came from)."
        },
        "right_label": {
          "type": "string",
          "description": "Optional short name for the second side, used as its column header."
        },
        "allow_unmatched": {
          "type": "boolean",
          "description": "Set to true only when the two key sets are genuinely expected to be disjoint. By default the comparison is refused when no keys match at all, because that means the two key columns do not describe the same kind of thing."
        }
      },
      "required": ["left_sql", "right_sql"],
      "additionalProperties": false
    }
    """);

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "RULE: any request to compare, reconcile, or diff a numeric figure across two tables or two uploaded files MUST use this tool. Never answer such a request by calling query_tabular_data on each side yourself and merging the two result sets in your response text. That manual merge is unreliable: when a key is missing on one side, you cannot tell whether it truly has no value there or whether the two sides just name or group that key differently, so writing $0 for it misrepresents real revenue as missing. This tool joins both sides in code instead, so it reports a key as genuinely unmatched rather than silently substituting zero, and it refuses outright (listing sample keys from each side) when nothing matches at all, which usually means the two key columns describe different things.";

    /// <summary>
    /// Gets the json schema.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional properties.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>()
        {
            ["Strict"] = false,
        };

    /// <summary>
    /// Invokes core.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetRequiredService<ILogger<CompareTabularDataTool>>();

        if (!arguments.TryGetFirstString("left_sql", out var leftSql) || string.IsNullOrWhiteSpace(leftSql))
        {
            return "A 'left_sql' query is required. It must return exactly two columns: the key to match on and the numeric measure to compare.";
        }

        if (!arguments.TryGetFirstString("right_sql", out var rightSql) || string.IsNullOrWhiteSpace(rightSql))
        {
            return "A 'right_sql' query is required. It must return exactly two columns: the key to match on and the numeric measure to compare.";
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "AI tool '{ToolName}' invoked. left_sql: {LeftSql} | right_sql: {RightSql}",
                Name,
                leftSql.SanitizeForLog(),
                rightSql.SanitizeForLog());
        }

        var preparation = await TabularToolRunner.PrepareAsync(arguments.Services, cancellationToken);

        if (preparation.Error is not null)
        {
            return preparation.Error;
        }

        using var workspace = preparation.Workspace;

        arguments.TryGetFirstString("left_label", out var leftLabel);
        arguments.TryGetFirstString("right_label", out var rightLabel);
        var allowUnmatched = arguments.GetFirstValueOrDefault("allow_unmatched", false);

        try
        {
            var left = await workspace.QueryAsync(leftSql, 0, cancellationToken);
            var right = await workspace.QueryAsync(rightSql, 0, cancellationToken);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "AI tool '{ToolName}' completed. left returned {LeftRowCount} row(s) and {LeftColumnCount} column(s); right returned {RightRowCount} row(s) and {RightColumnCount} column(s).",
                    Name,
                    left.Rows.Count,
                    left.Columns.Count,
                    right.Rows.Count,
                    right.Columns.Count);
            }

            return BuildComparison(left, right, leftLabel, rightLabel, allowUnmatched);
        }
        catch (TabularSqlException ex)
        {
            return ex.Message;
        }
        catch (SqliteException ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "Tabular comparison failed for tool '{ToolName}'.", Name);
            }

            return TabularSqlErrorFormatter.Format("The comparison could not be executed", ex, preparation.Tables);
        }
    }

    internal static string BuildComparison(TabularQueryResult left, TabularQueryResult right, string leftLabel, string rightLabel, bool allowUnmatched)
    {
        var leftName = string.IsNullOrWhiteSpace(leftLabel) ? "left" : leftLabel.Trim();

        var rightName = string.IsNullOrWhiteSpace(rightLabel) ? "right" : rightLabel.Trim();

        if (string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase))
        {
            leftName = string.Concat(leftName, " (1)");
            rightName = string.Concat(rightName, " (2)");
        }

        if (!TryBuildSide(left, "left_sql", out var leftSide, out var error) || !TryBuildSide(right, "right_sql", out var rightSide, out error))
        {
            return error;
        }

        if (leftSide.Count == 0 && rightSide.Count == 0)
        {
            return "Both queries returned no rows, so there is nothing to compare.";
        }

        var matched = leftSide.Keys.Where(rightSide.ContainsKey).ToList();

        if (matched.Count == 0 && !allowUnmatched)
        {
            return BuildNoMatchMessage(leftSide, rightSide, leftName, rightName);
        }

        using var builder = ZString.CreateStringBuilder();

        builder.Append("Compared \"");
        builder.Append(leftName);
        builder.Append("\" to \"");
        builder.Append(rightName);
        builder.Append("\" on ");
        builder.Append(matched.Count);
        builder.Append(matched.Count == 1 ? " matching key" : " matching keys");
        builder.AppendLine(".");

        if (matched.Count > 0)
        {
            var rows = matched
                .Select(key => (
                    Display: leftSide[key].Display,
                    Left: leftSide[key].Value,
                    Right: rightSide[key].Value,
                    Difference: leftSide[key].Value - rightSide[key].Value))
                .OrderByDescending(row => row.Difference)
                .ToList();

            builder.AppendLine();
            builder.Append("key | ");
            builder.Append(leftName);
            builder.Append(" | ");
            builder.Append(rightName);
            builder.AppendLine(" | difference");

            foreach (var row in rows.Take(MaxComparedRows))
            {
                builder.Append(row.Display);
                builder.Append(" | ");
                builder.Append(TabularResultAnalyzer.FormatNumber(row.Left));
                builder.Append(" | ");
                builder.Append(TabularResultAnalyzer.FormatNumber(row.Right));
                builder.Append(" | ");
                builder.AppendLine(TabularResultAnalyzer.FormatNumber(row.Difference));
            }

            if (rows.Count > MaxComparedRows)
            {
                builder.Append("… ");
                builder.Append(rows.Count - MaxComparedRows);
                builder.AppendLine(" more matching row(s) not shown; narrow the queries to see them.");
            }
        }

        var leftUnmatched = FormatUnmatched(leftSide, rightSide, leftName);

        var rightUnmatched = FormatUnmatched(rightSide, leftSide, rightName);

        if (leftUnmatched is not null)
        {
            builder.AppendLine();
            builder.AppendLine(leftUnmatched);
        }

        if (rightUnmatched is not null)
        {
            builder.AppendLine();
            builder.AppendLine(rightUnmatched);
        }

        var leftTotal = leftSide.Values.Sum(entry => entry.Value);

        var rightTotal = rightSide.Values.Sum(entry => entry.Value);

        builder.AppendLine();
        builder.Append("Totals across all rows — ");
        builder.Append(leftName);
        builder.Append(": ");
        builder.Append(TabularResultAnalyzer.FormatNumber(leftTotal));
        builder.Append(" | ");
        builder.Append(rightName);
        builder.Append(": ");
        builder.Append(TabularResultAnalyzer.FormatNumber(rightTotal));
        builder.Append(" | difference: ");
        builder.AppendLine(TabularResultAnalyzer.FormatNumber(leftTotal - rightTotal));

        return builder.ToString();
    }

    private static string FormatUnmatched(Dictionary<string, SideEntry> side, Dictionary<string, SideEntry> other, string label)
    {
        var unmatched = side
            .Where(pair => !other.ContainsKey(pair.Key))
            .Select(pair => pair.Value.Display)
            .ToList();

        if (unmatched.Count == 0)
        {
            return null;
        }

        using var builder = ZString.CreateStringBuilder();

        builder.Append("Unmatched, only in \"");
        builder.Append(label);
        builder.Append("\" (");
        builder.Append(unmatched.Count);
        builder.Append("): ");
        builder.Append(string.Join(", ", unmatched.Take(MaxListedKeys)));

        if (unmatched.Count > MaxListedKeys)
        {
            builder.Append(", … ");
            builder.Append(unmatched.Count - MaxListedKeys);
            builder.Append(" more");
        }

        return builder.ToString();
    }

    private static string BuildNoMatchMessage(Dictionary<string, SideEntry> leftSide, Dictionary<string, SideEntry> rightSide, string leftName, string rightName)
    {
        using var builder = ZString.CreateStringBuilder();

        builder.Append("None of the ");
        builder.Append(leftSide.Count);
        builder.Append(" key(s) from \"");
        builder.Append(leftName);
        builder.Append("\" matched any of the ");
        builder.Append(rightSide.Count);
        builder.Append(" key(s) from \"");
        builder.Append(rightName);
        builder.AppendLine("\", so no comparison was produced.");

        builder.Append(leftName);
        builder.Append(" keys (sample): ");
        builder.AppendLine(string.Join(", ", leftSide.Values.Take(SampleKeyCount).Select(entry => entry.Display)));

        builder.Append(rightName);
        builder.Append(" keys (sample): ");
        builder.AppendLine(string.Join(", ", rightSide.Values.Take(SampleKeyCount).Select(entry => entry.Display)));

        builder.Append("These two key columns do not describe the same kind of thing. Pick key columns that hold comparable values (check the column list from list_tabular_data) and run this again, or pass allow_unmatched: true if the two sets really are disjoint.");

        return builder.ToString();
    }

    private static bool TryBuildSide(TabularQueryResult result, string argumentName, out Dictionary<string, SideEntry> side, out string error)
    {
        side = null;

        if (result.Columns.Count != 2)
        {
            error = $"The '{argumentName}' query returned {result.Columns.Count} column(s) ({string.Join(", ", result.Columns)}), but exactly two are required: the key to match on first, then the numeric measure.";

            return false;
        }

        var entries = new Dictionary<string, SideEntry>(StringComparer.Ordinal);

        foreach (var row in result.Rows)
        {
            var display = FormatKey(row[0]);

            var normalized = NormalizeKey(display);

            if (normalized.Length == 0)
            {
                continue;
            }

            if (!TabularResultAnalyzer.TryGetNumber(row[1], out var value))
            {
                error = $"The '{argumentName}' query returned the non-numeric value '{row[1]}' in its second column ({result.Columns[1]}). The second column must be the numeric measure to compare.";

                return false;
            }

            if (entries.TryGetValue(normalized, out var existing))
            {
                existing.Value += value;
            }
            else
            {
                entries[normalized] = new SideEntry
                {
                    Display = display,
                    Value = value,
                };
            }
        }

        side = entries;

        error = null;

        return true;
    }

    private static string FormatKey(object value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text.Trim(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture).Trim(),
            _ => value.ToString().Trim(),
        };
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        using var builder = ZString.CreateStringBuilder();

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private sealed class SideEntry
    {
        public string Display { get; set; }

        public double Value { get; set; }
    }
}
