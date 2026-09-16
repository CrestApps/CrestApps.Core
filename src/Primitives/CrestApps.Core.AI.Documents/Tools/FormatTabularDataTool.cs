using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// Records how a tabular table should be presented when it is exported to a spreadsheet: number
/// formats such as currency and dates, fonts and colors, conditional formatting, live formulas,
/// a total row, and embedded charts.
/// <para>
/// The specification is stored alongside the workspace data, so formatting requested in one turn is
/// still applied when the file is exported in a later one, and a follow-up request refines the stored
/// specification instead of replacing everything the user already asked for.
/// </para>
/// </summary>
public sealed class FormatTabularDataTool : AIFunction
{
    public const string TheName = TabularToolNames.FormatTabularData;

    private const string InvocationCountKey = nameof(FormatTabularDataTool) + ".InvocationCount";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "table_name": {
          "type": "string",
          "description": "Optional SQL table name from list_tabular_data. OMIT this in almost every case: the formatting then applies to whatever the next export produces, which is what you want when the export is a query that joins or reshapes tables. Only set it to format one specific source table exported on its own."
        },
        "sheet_name": {
          "type": "string",
          "description": "Optional worksheet name for the exported file, for example 'Variance Report'."
        },
        "replace": {
          "type": "boolean",
          "description": "Set to true to discard the formatting already recorded for this table before applying this request. Defaults to false, which merges this request into the existing formatting so earlier instructions are kept."
        },
        "clear": {
          "type": "boolean",
          "description": "Set to true to remove all recorded formatting for this table and export it unformatted."
        },
        "columns": {
          "type": "array",
          "description": "Per-column presentation. Naming a column that is not in the table AND supplying 'formula' appends it as a new calculated column.",
          "items": {
            "type": "object",
            "properties": {
              "column": { "type": "string", "description": "The column header this applies to." },
              "format": {
                "type": "string",
                "description": "Number presentation: 'currency', 'accounting', 'number', 'percent', 'date', 'datetime', 'time', 'duration', 'scientific', or 'text'. Percent expects values stored as fractions, so 0.15 renders as 15%.",
                "enum": ["general", "text", "number", "currency", "accounting", "percent", "scientific", "date", "datetime", "time", "duration"]
              },
              "decimals": { "type": "integer", "description": "Decimal places. Defaults to 2 for currency and accounting, 0 otherwise." },
              "currency_symbol": { "type": "string", "description": "Currency symbol, for example '$' or 'EUR'. Defaults to '$'." },
              "negatives_in_red": { "type": "boolean", "description": "Render negative values in red and in parentheses." },
              "format_code": { "type": "string", "description": "An explicit spreadsheet format code, for use when none of the named formats fit. Overrides 'format'." },
              "data_kind": {
                "type": "string",
                "description": "Force how values are stored. Use 'text' for identifiers such as zip codes and account numbers whose leading zeros must survive. Numeric and date columns are detected automatically, so this is rarely needed.",
                "enum": ["auto", "text", "number", "date", "boolean"]
              },
              "width": { "type": "number", "description": "Column width in characters. Measured from the content when omitted." },
              "formula": { "type": "string", "description": "A live spreadsheet formula written into every row of this column. Reference other columns by name in braces, for example '={Actual}-{Planned}'. Use '{row}' for the row number. This is how a calculated column is added." },
              "bold": { "type": "boolean" },
              "italic": { "type": "boolean" },
              "font_color": { "type": "string", "description": "Hex color, for example '#9C0006'." },
              "background_color": { "type": "string", "description": "Hex fill color, for example '#FFC7CE'." },
              "align": { "type": "string", "enum": ["left", "center", "right"] },
              "wrap": { "type": "boolean" },
              "border": { "type": "boolean" }
            },
            "required": ["column"],
            "additionalProperties": false
          }
        },
        "header": {
          "type": "object",
          "description": "Header row presentation. The header is styled, frozen, and given filter dropdowns by default.",
          "properties": {
            "style": { "type": "boolean", "description": "Set to false to leave the header unstyled." },
            "freeze": { "type": "boolean", "description": "Keep the header visible while scrolling. Defaults to true." },
            "filter": { "type": "boolean", "description": "Add filter dropdowns to the header. Defaults to true." },
            "bold": { "type": "boolean" },
            "font_color": { "type": "string" },
            "background_color": { "type": "string" },
            "align": { "type": "string", "enum": ["left", "center", "right"] },
            "wrap": { "type": "boolean" }
          },
          "additionalProperties": false
        },
        "banded_rows": { "type": "boolean", "description": "Shade alternating rows to make wide tables easier to read." },
        "band_color": { "type": "string", "description": "Hex fill color for the shaded rows. Defaults to a light gray." },
        "conditional_formats": {
          "type": "array",
          "description": "Rules that color cells based on their value.",
          "items": {
            "type": "object",
            "properties": {
              "column": { "type": "string" },
              "rule": {
                "type": "string",
                "description": "'color_scale' applies a gradient across the column's range and is the right choice for 'highlight the biggest and smallest values'. 'data_bar' draws in-cell bars.",
                "enum": ["greater_than", "less_than", "equal_to", "between", "contains_text", "duplicate_values", "color_scale", "data_bar", "icon_set"]
              },
              "value": { "type": "string", "description": "The comparison value, or the text to search for." },
              "second_value": { "type": "string", "description": "The upper bound, for 'between'." },
              "min_color": { "type": "string", "description": "Gradient color for the lowest value." },
              "mid_color": { "type": "string", "description": "Gradient color for the midpoint. Supplying it makes the gradient three-stop." },
              "max_color": { "type": "string", "description": "Gradient color for the highest value." },
              "bar_color": { "type": "string" },
              "icon_set": { "type": "string", "description": "Icon set name, for example '3TrafficLights1' or '3Arrows'." },
              "font_color": { "type": "string", "description": "Text color applied to matching cells." },
              "background_color": { "type": "string", "description": "Fill color applied to matching cells." },
              "bold": { "type": "boolean" }
            },
            "required": ["column", "rule"],
            "additionalProperties": false
          }
        },
        "total_row": {
          "type": "object",
          "description": "A total row appended below the data, written as live formulas so it recalculates when the reader edits or filters the sheet.",
          "properties": {
            "label": { "type": "string", "description": "Label for the first column, for example 'Grand Total'." },
            "columns": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "column": { "type": "string" },
                  "function": { "type": "string", "enum": ["sum", "average", "count", "min", "max"] }
                },
                "required": ["column"],
                "additionalProperties": false
              }
            }
          },
          "additionalProperties": false
        },
        "charts": {
          "type": "array",
          "description": "Charts embedded in the exported spreadsheet, bound to the sheet's cells so they redraw when the data changes. To show a chart in the chat instead, use generate_chart.",
          "items": {
            "type": "object",
            "properties": {
              "type": { "type": "string", "enum": ["column", "bar", "line", "pie", "area"] },
              "title": { "type": "string" },
              "category_column": { "type": "string", "description": "The column supplying the axis labels." },
              "value_columns": { "type": "array", "items": { "type": "string" }, "description": "The columns plotted as series." },
              "max_categories": { "type": "integer", "description": "How many rows to plot. Defaults to 25, because a chart with more categories than that is unreadable." },
              "width": { "type": "integer" },
              "height": { "type": "integer" }
            },
            "additionalProperties": false
          }
        }
      },
      "required": [],
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
    public override string Description => "Records how the tabular data should look when exported to a spreadsheet: currency/number/percent/date formats, fonts and cell colors, column widths, frozen header, filters, conditional formatting (including gradient color scales and data bars), calculated columns written as live spreadsheet formulas, a total row, and embedded charts. Call this whenever the user asks for formatting, styling, highlighting, a calculated column, a total row, or a chart inside the file, then call export_tabular_data to produce the formatted file. The formatting is remembered for the rest of the conversation, so a later request only needs to describe what changes.";

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
        var logger = arguments.Services.GetRequiredService<ILogger<FormatTabularDataTool>>();
        var invocationNumber = IncrementInvocationCount();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("AI tool '{ToolName}' invoked (call #{InvocationNumber}).", Name, invocationNumber);
        }

        arguments.TryGetFirstString("table_name", out var requestedTableName);

        var preparation = await TabularToolRunner.PrepareAsync(arguments.Services, cancellationToken);

        if (preparation.Error is not null)
        {
            return preparation.Error;
        }

        using var workspace = preparation.Workspace;
        var table = ResolveTable(preparation.Tables, requestedTableName);

        if (table is null && !string.IsNullOrWhiteSpace(requestedTableName))
        {
            return $"The table '{requestedTableName}' was not found. Use list_tabular_data to find the available table names.";
        }

        // With no table named, the formatting belongs to whatever the next export produces. That is the
        // common case for a report built by joining several tables, where no single source table owns
        // the result.
        var formattingKey = table?.TableName ?? TabularToolNames.WorkspaceFormattingKey;

        if (TryGetBoolean(arguments, "clear"))
        {
            await workspace.SaveFormattingAsync(formattingKey, specJson: null, cancellationToken);

            return $"Cleared the recorded formatting for {DescribeTarget(table)}. The next export will be unformatted.";
        }

        var request = SpreadsheetFormattingJson.Parse(ToJsonElement(arguments));

        var existing = TryGetBoolean(arguments, "replace")
            ? null
            : SpreadsheetFormattingJson.Deserialize((await workspace.GetFormattingAsync(formattingKey, cancellationToken)).SpecJson);

        var merged = SpreadsheetFormattingMerge.Merge(existing, request);
        var revision = await workspace.SaveFormattingAsync(
            formattingKey,
            SpreadsheetFormattingJson.Serialize(merged),
            cancellationToken);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "AI tool '{ToolName}' completed (call #{InvocationNumber}). Target='{TableName}', Columns={ColumnCount}, ConditionalFormats={ConditionalCount}, Charts={ChartCount}, Revision={Revision}.",
                Name,
                invocationNumber,
                formattingKey,
                merged.Columns.Count,
                merged.ConditionalFormats.Count,
                merged.Charts.Count,
                revision);
        }

        return BuildSummary(DescribeTarget(table), merged, WarnAboutUnknownColumns(table, preparation.Tables, merged));
    }

    private static string DescribeTarget(TabularTableInfo table)
    {
        return table is null
            ? "the next exported file"
            : $"table \"{table.TableName}\"";
    }

    private static string BuildSummary(string target, SpreadsheetFormatting formatting, string warning)
    {
        var builder = new StringBuilder();
        builder.Append("Recorded the spreadsheet formatting for ");
        builder.Append(target);
        builder.Append(": ");

        var parts = new List<string>();

        if (formatting.Columns.Count > 0)
        {
            var computed = formatting.Columns.Count(column => !string.IsNullOrWhiteSpace(column.Formula));

            parts.Add($"{formatting.Columns.Count} column format(s)");

            if (computed > 0)
            {
                parts.Add($"{computed} formula column(s)");
            }
        }

        if (formatting.ConditionalFormats.Count > 0)
        {
            parts.Add($"{formatting.ConditionalFormats.Count} conditional rule(s)");
        }

        if (formatting.TotalRow is not null)
        {
            parts.Add("a total row");
        }

        if (formatting.Charts.Count > 0)
        {
            parts.Add($"{formatting.Charts.Count} chart(s)");
        }

        if (formatting.BandedRows == true)
        {
            parts.Add("banded rows");
        }

        builder.Append(parts.Count == 0
            ? "sheet defaults only"
            : string.Join(", ", parts));

        builder.Append(". Call export_tabular_data to produce the formatted file; do not describe the formatting as done until that export succeeds.");

        if (!string.IsNullOrEmpty(warning))
        {
            builder.Append(' ');
            builder.Append(warning);
        }

        return builder.ToString();
    }

    private static string WarnAboutUnknownColumns(
        TabularTableInfo table,
        IReadOnlyList<TabularTableInfo> allTables,
        SpreadsheetFormatting formatting)
    {
        // Formatting recorded for the export as a whole is checked against nothing: the export's headers
        // are whatever its query aliases them to, so a name that matches no source column is normal
        // rather than a mistake. Warning here would push the model to "correct" names that were right.
        if (table is null)
        {
            return null;
        }

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in table.Columns)
        {
            known.Add(column.Name);

            if (!string.IsNullOrEmpty(column.SourceName))
            {
                known.Add(column.SourceName);
            }
        }

        var unknown = new List<string>();

        foreach (var column in formatting.Columns)
        {
            // A column with a formula is meant to be new, so it is not reported as unknown.
            if (!string.IsNullOrWhiteSpace(column.Column) &&
                string.IsNullOrWhiteSpace(column.Formula) &&
                !known.Contains(column.Column.Trim()))
            {
                unknown.Add(column.Column.Trim());
            }
        }

        if (unknown.Count == 0)
        {
            return null;
        }

        // Naming the mismatch is what lets the model correct itself; silently dropping the format is
        // how a user ends up being told their file was formatted when it was not.
        return $"Note: no column named {string.Join(", ", unknown.Select(name => $"\"{name}\""))} exists in this table, so that formatting will not apply. Exports use the original source headers, so use those names (see get_document_metadata with scope \"headers\").";
    }

    private static JsonElement ToJsonElement(AIFunctionArguments arguments)
    {
        // The arguments arrive as a loosely typed dictionary. Round-tripping them through JSON gives
        // one shape to parse, whether the model sent nested objects or a serialized string.
        var document = JsonSerializer.SerializeToElement(arguments.ToDictionary(pair => pair.Key, pair => pair.Value));

        return document;
    }

    private static bool TryGetBoolean(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return false;
        }

        return value switch
        {
            bool flag => flag,
            string text => bool.TryParse(text, out var parsed) && parsed,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.String } element => bool.TryParse(element.GetString(), out var parsed) && parsed,
            _ => false,
        };
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
}
