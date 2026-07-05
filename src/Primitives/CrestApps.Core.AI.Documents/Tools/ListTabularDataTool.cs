using System.Text.Json;
using CrestApps.Core.AI.Documents.Tabular;
using Cysharp.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// Tool that lists the tabular tables available in the active conversation, along with their
/// source file, row count, and columns, so the model can compose SQL queries against them.
/// </summary>
public sealed class ListTabularDataTool : AIFunction
{
    public const string TheName = TabularToolNames.ListTabularData;

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {},
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
    public override string Description => "Lists the tabular tables (from uploaded CSV, TSV, or Excel files) available in this conversation, including their columns and row counts, so they can be queried with SQL.";

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
        var logger = arguments.Services.GetRequiredService<ILogger<ListTabularDataTool>>();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked.", Name);
        }

        var preparation = await TabularToolRunner.PrepareAsync(arguments.Services, cancellationToken);

        if (preparation.Error is not null)
        {
            return preparation.Error;
        }

        using var workspace = preparation.Workspace;
        var tables = preparation.Tables;

        if (tables is null || tables.Count == 0)
        {
            return "No tabular files (CSV, TSV, or Excel) are attached to this conversation.";
        }

        using var builder = ZString.CreateStringBuilder();

        builder.Append("This conversation has ");
        builder.Append(tables.Count);
        builder.AppendLine(tables.Count == 1 ? " tabular table you can query with SQL (SQLite dialect)." : " tabular tables you can query with SQL (SQLite dialect).");
        builder.AppendLine("All values are stored as TEXT; CAST when you need numeric or date operations.");
        builder.AppendLine();

        foreach (var table in tables)
        {
            builder.Append("Table \"");
            builder.Append(table.TableName);
            builder.Append('"');

            if (!string.IsNullOrEmpty(table.SourceFileName))
            {
                builder.Append(" (from \"");
                builder.Append(table.SourceFileName);
                builder.Append("\")");
            }

            builder.Append(" — ");
            builder.Append(table.RowCount);
            builder.AppendLine(table.RowCount == 1 ? " row" : " rows");

            builder.Append("  Columns: ");
            builder.AppendLine(string.Join(", ", table.Columns.Select(FormatColumn)));
        }

        builder.AppendLine();
        builder.Append("Use ");
        builder.Append(TabularToolNames.QueryTabularData);
        builder.Append(" to run SELECT statements (aggregate or filter to keep results small), ");
        builder.Append(TabularToolNames.ExecuteTabularCommand);
        builder.Append(" to modify the in-memory copy, ");
        builder.Append(TabularToolNames.FillEmptyTabularCells);
        builder.Append(" to replace every blank/empty cell in a table with one value using one fast bulk update, and ");
        builder.Append(TabularToolNames.ExportTabularData);
        builder.AppendLine(" to create a downloadable CSV from a SELECT query. The originally uploaded file is always preserved.");

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' completed.", Name);
        }

        return builder.ToString();
    }

    private static string FormatColumn(TabularColumnInfo column)
    {
        if (string.IsNullOrWhiteSpace(column.SourceName) ||
            string.Equals(column.Name, column.SourceName, StringComparison.OrdinalIgnoreCase))
        {
            return column.Name;
        }

        return $"{column.Name} (source header: {column.SourceName})";
    }
}
