using System.Text.Json;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Extensions;
using Cysharp.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// Tool that runs a read-only SQL query against the in-memory tabular workspace and returns a
/// compact result set, keeping token usage low even for very large source files.
/// </summary>
public sealed class QueryTabularDataTool : AIFunction
{
    public const string TheName = TabularToolNames.QueryTabularData;

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "sql": {
          "type": "string",
          "description": "A single read-only SQL query (SELECT or WITH ... SELECT) in SQLite dialect, referencing the tables returned by list_tabular_data."
        },
        "max_rows": {
          "type": "integer",
          "description": "Maximum number of rows to return. Prefer aggregation and small limits to keep results compact."
        }
      },
      "required": ["sql"],
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
    public override string Description => "Runs a read-only SQL query (SELECT) against the uploaded tabular data and returns a compact result. Use aggregation and filtering instead of selecting all rows.";

    /// <summary>
    /// Gets the json Schema.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional Properties.
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
        var logger = arguments.Services.GetRequiredService<ILogger<QueryTabularDataTool>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        if (!arguments.TryGetFirstString("sql", out var sql) || string.IsNullOrWhiteSpace(sql))
        {
            return "A 'sql' query is required.";
        }

        var preparation = await TabularToolRunner.PrepareAsync(arguments.Services, cancellationToken);

        if (preparation.Error is not null)
        {
            return preparation.Error;
        }

        var maxRows = arguments.GetFirstValueOrDefault("max_rows", 0);

        try
        {
            var result = await preparation.Workspace.QueryAsync(sql, maxRows, cancellationToken);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AI tool '{ToolName}' completed.", Name);
            }

            return FormatResult(result);
        }
        catch (TabularSqlException ex)
        {
            return ex.Message;
        }
        catch (SqliteException ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "Tabular query failed for tool '{ToolName}'.", Name);
            }

            return $"The query could not be executed: {ex.Message}";
        }
    }

    private static string FormatResult(TabularQueryResult result)
    {
        if (result.Rows.Count == 0)
        {
            return "The query returned no rows.";
        }

        using var builder = ZString.CreateStringBuilder();

        builder.Append("Returned ");
        builder.Append(result.Rows.Count);
        builder.Append(result.Rows.Count == 1 ? " row" : " rows");

        if (result.Truncated)
        {
            builder.Append(" (truncated to the row limit; refine the query with aggregation or LIMIT for the full picture)");
        }

        builder.AppendLine(":");
        builder.AppendLine();
        builder.AppendLine(string.Join(" | ", result.Columns));

        foreach (var row in result.Rows)
        {
            builder.AppendLine(string.Join(" | ", row.Select(cell => cell ?? string.Empty)));
        }

        return builder.ToString();
    }
}
