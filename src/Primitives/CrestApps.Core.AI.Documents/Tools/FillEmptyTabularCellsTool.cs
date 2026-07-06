using System.Text;
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
/// Replaces every empty cell in a tabular workspace table with a supplied value using one set-based
/// <c>UPDATE</c> statement rather than many per-column or per-cell commands.
/// </summary>
public sealed class FillEmptyTabularCellsTool : AIFunction
{
    public const string TheName = TabularToolNames.FillEmptyTabularCells;
    private const string InvocationCountKey = nameof(FillEmptyTabularCellsTool) + ".InvocationCount";
    internal const int DefaultMaxColumnsPerStatement = 64;

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "replacement_value": {
          "type": "string",
          "description": "The string that should replace every empty cell in the selected table (for example 'NULL')."
        },
        "table_name": {
          "type": "string",
          "description": "Optional SQL table name from list_tabular_data. Omit this when only one table is loaded."
        }
      },
      "required": ["replacement_value"],
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
    public override string Description => "Replaces every empty cell in a loaded tabular table with a supplied value using one set-based update. Use this when the user wants blanks/empty cells filled across the whole uploaded file (for example replacing all empty cells with 'NULL') instead of generating many UPDATE statements.";

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
        var logger = arguments.Services.GetRequiredService<ILogger<FillEmptyTabularCellsTool>>();
        var invocationNumber = IncrementInvocationCount();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked (call #{InvocationNumber}).", Name, invocationNumber);
        }

        if (!arguments.TryGetFirstString("replacement_value", allowEmptyString: true, out var replacementValue))
        {
            return "A 'replacement_value' string is required.";
        }

        arguments.TryGetFirstString("table_name", out var requestedTableName);

        var preparation = await TabularToolRunner.PrepareAsync(arguments.Services, cancellationToken);

        if (preparation.Error is not null)
        {
            return preparation.Error;
        }

        using var workspace = preparation.Workspace;
        var table = ResolveTable(preparation.Tables, requestedTableName);

        if (table is null)
        {
            return string.IsNullOrWhiteSpace(requestedTableName)
                ? "Multiple tabular tables are loaded. Provide 'table_name' from list_tabular_data so the empty-cell replacement knows which table to update."
                : $"The table '{requestedTableName}' was not found. Use list_tabular_data to find the available table names.";
        }

        if (table.Columns.Count == 0)
        {
            return $"The table '{table.TableName}' has no columns to update.";
        }

        var statements = BuildStatements(table, replacementValue);

        try
        {
            var result = await workspace.ExecuteAsync(string.Join(";", statements), cancellationToken);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "AI tool '{ToolName}' completed (call #{InvocationNumber}). Table='{TableName}', Columns={ColumnCount}, InternalBatches={InternalBatchCount}, Statements={StatementCount}, AffectedRows={AffectedRows}.",
                    Name,
                    invocationNumber,
                    table.TableName,
                    table.Columns.Count,
                    statements.Count,
                    result.StatementCount,
                    result.AffectedRows);
            }

            return statements.Count == 1
                ? $"Filled empty cells in table \"{table.TableName}\" with \"{replacementValue}\". {result.AffectedRows} row(s) were affected."
                : $"Filled empty cells in table \"{table.TableName}\" with \"{replacementValue}\" across all columns using {statements.Count} internal bulk updates. {result.AffectedRows} row-update operation(s) were affected.";
        }
        catch (TabularSqlException ex)
        {
            return ex.Message;
        }
        catch (SqliteException ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "Tabular empty-cell fill failed for tool '{ToolName}'.", Name);
            }

            return $"The empty-cell replacement could not be executed: {ex.Message}";
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

    private static TabularTableInfo ResolveTable(
        IReadOnlyList<TabularTableInfo> tables,
        string requestedTableName)
    {
        if (tables.Count == 1 && string.IsNullOrWhiteSpace(requestedTableName))
        {
            return tables[0];
        }

        if (string.IsNullOrWhiteSpace(requestedTableName))
        {
            return null;
        }

        return tables.FirstOrDefault(table => string.Equals(table.TableName, requestedTableName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<string> BuildStatements(
        TabularTableInfo table,
        string replacementValue,
        int maxColumnsPerStatement = DefaultMaxColumnsPerStatement)
    {
        ArgumentNullException.ThrowIfNull(table);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxColumnsPerStatement);

        var escapedValue = EscapeSqlStringLiteral(replacementValue ?? string.Empty);
        var statements = new List<string>();

        for (var batchStart = 0; batchStart < table.Columns.Count; batchStart += maxColumnsPerStatement)
        {
            var batchSize = Math.Min(maxColumnsPerStatement, table.Columns.Count - batchStart);
            var assignments = new StringBuilder();
            var predicates = new StringBuilder();

            for (var offset = 0; offset < batchSize; offset++)
            {
                var column = table.Columns[batchStart + offset];
                var quotedName = QuoteIdentifier(column.Name);

                if (offset > 0)
                {
                    assignments.Append(", ");
                    predicates.Append(" OR ");
                }

                assignments.Append(quotedName);
                assignments.Append(" = CASE WHEN ");
                assignments.Append(quotedName);
                assignments.Append(" IS NULL OR TRIM(");
                assignments.Append(quotedName);
                assignments.Append(") = '' THEN '");
                assignments.Append(escapedValue);
                assignments.Append("' ELSE ");
                assignments.Append(quotedName);
                assignments.Append(" END");

                predicates.Append(quotedName);
                predicates.Append(" IS NULL OR TRIM(");
                predicates.Append(quotedName);
                predicates.Append(") = ''");
            }

            statements.Add($"UPDATE {QuoteIdentifier(table.TableName)} SET {assignments} WHERE {predicates}");
        }

        return statements;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier?.Replace("\"", "\"\"", StringComparison.Ordinal) ?? string.Empty}\"";
    }

    private static string EscapeSqlStringLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
