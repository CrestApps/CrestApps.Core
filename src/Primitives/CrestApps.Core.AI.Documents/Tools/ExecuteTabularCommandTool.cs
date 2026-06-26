using System.Text.Json;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// Tool that runs a single data-manipulation or schema statement (for example
/// <c>INSERT</c>, <c>UPDATE</c>, <c>DELETE</c>, or <c>ALTER TABLE</c>) against the in-memory
/// tabular workspace. The originally uploaded file is never modified; changes apply only to the
/// in-memory copy and are discarded when the prompt completes.
/// </summary>
public sealed class ExecuteTabularCommandTool : AIFunction
{
    public const string TheName = TabularToolNames.ExecuteTabularCommand;

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "sql": {
          "type": "string",
          "description": "A single SQLite data-manipulation or schema statement (INSERT, UPDATE, DELETE, REPLACE, ALTER, CREATE, or DROP) to apply to the in-memory copy of the data."
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
    public override string Description => "Applies a single SQL manipulation or schema change (INSERT, UPDATE, DELETE, ALTER TABLE, etc.) to the in-memory copy of the uploaded tabular data. The original file is preserved.";

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
        var logger = arguments.Services.GetRequiredService<ILogger<ExecuteTabularCommandTool>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        if (!arguments.TryGetFirstString("sql", out var sql) || string.IsNullOrWhiteSpace(sql))
        {
            return "A 'sql' statement is required.";
        }

        var preparation = await TabularToolRunner.PrepareAsync(arguments.Services, cancellationToken);

        if (preparation.Error is not null)
        {
            return preparation.Error;
        }

        try
        {
            var result = await preparation.Workspace.ExecuteAsync(sql, cancellationToken);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("AI tool '{ToolName}' completed.", Name);
            }

            return $"Statement executed against the in-memory copy. {result.AffectedRows} row(s) affected. The original uploaded file is unchanged.";
        }
        catch (TabularSqlException ex)
        {
            return ex.Message;
        }
        catch (SqliteException ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "Tabular command failed for tool '{ToolName}'.", Name);
            }

            return $"The statement could not be executed: {ex.Message}";
        }
    }
}
