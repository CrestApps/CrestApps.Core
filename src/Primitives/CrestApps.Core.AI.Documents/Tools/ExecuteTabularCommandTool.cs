using System.Text.Json;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// Tool that runs one or more data-manipulation or schema statements (for example
/// <c>INSERT</c>, <c>UPDATE</c>, <c>DELETE</c>, or <c>ALTER TABLE</c>) against the in-memory
/// tabular workspace in a single batch. The originally uploaded file is never modified; changes apply
/// to the in-memory copy and are persisted as the conversation's working copy so they survive workspace
/// rebuilds and can be exported later.
/// </summary>
public sealed class ExecuteTabularCommandTool : AIFunction
{
    public const string TheName = TabularToolNames.ExecuteTabularCommand;
    private const string InvocationCountKey = nameof(ExecuteTabularCommandTool) + ".InvocationCount";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "sql": {
          "type": "string",
          "description": "One or more SQLite data-manipulation or schema statements (INSERT, UPDATE, DELETE, REPLACE, ALTER, CREATE, or DROP), separated by semicolons, to apply to the in-memory copy of the data. Submit every change needed in a single call using set-based statements rather than one statement per row or cell. All statements run in one transaction and roll back together if any fails."
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
    public override string Description => "Applies one or more SQL manipulation or schema changes (INSERT, UPDATE, DELETE, ALTER TABLE, etc.) to the in-memory copy of the uploaded tabular data in a single transactional batch. Batch every change into one call using set-based statements instead of one statement per row or cell. The original file is preserved.";

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
        var invocationNumber = IncrementInvocationCount();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked (call #{InvocationNumber}).", Name, invocationNumber);
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

            SchedulePersistWorkspaceSnapshot(arguments.Services, preparation.Workspace, logger);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "AI tool '{ToolName}' completed (call #{InvocationNumber}). Documents={DocumentCount}, Tables={TableCount}, Statements={StatementCount}, AffectedRows={AffectedRows}.",
                    Name,
                    invocationNumber,
                    preparation.Context.Documents.Count,
                    preparation.Tables.Count,
                    result.StatementCount,
                    result.AffectedRows);
            }

            return $"Batch executed against the in-memory copy. {result.StatementCount} statement(s) ran and {result.AffectedRows} row(s) were affected. The original uploaded file is unchanged.";
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

    private static int IncrementInvocationCount()
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null)
        {
            return 1;
        }

        if (!invocationContext.Items.TryGetValue(InvocationCountKey, out var countObject) ||
            countObject is not int count)
        {
            count = 0;
        }

        count++;
        invocationContext.Items[InvocationCountKey] = count;

        return count;
    }

    private static void SchedulePersistWorkspaceSnapshot(
        IServiceProvider services,
        TabularWorkspace workspace,
        ILogger logger)
    {
        // Persist the mutated in-memory state so the changes survive workspace eviction, a process
        // restart, or being served by another application instance. This is scheduled as a coalesced
        // background operation rather than awaited so that a burst of commands does not each block on a
        // full-table snapshot, serialization, and write. The artifact store is a singleton, so it is
        // safe to capture here and use after the request scope completes.
        var artifactStore = services.GetService<ITabularDocumentArtifactStore>();

        if (artifactStore is null)
        {
            return;
        }

        workspace.SchedulePersist(token => PersistWorkspaceSnapshotAsync(workspace, artifactStore, logger, token));
    }

    private static async Task PersistWorkspaceSnapshotAsync(
        TabularWorkspace workspace,
        ITabularDocumentArtifactStore artifactStore,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshots = await workspace.SnapshotAsync(cancellationToken);

            foreach (var (documentId, artifact) in snapshots)
            {
                await artifactStore.SaveAsync(documentId, artifact, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "Failed to persist the in-memory tabular workspace snapshot for tool '{ToolName}'.", TheName);
            }
        }
    }
}
