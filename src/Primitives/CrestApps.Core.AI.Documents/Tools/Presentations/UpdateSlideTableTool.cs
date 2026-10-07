using System.Globalization;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Edits a table on a slide: its cells, rows, columns, widths and style, or all its rows from a query.
/// </summary>
internal sealed class UpdateSlideTableTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.UpdateSlideTable;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateSlideTableTool"/> class.
    /// </summary>
    public UpdateSlideTableTool()
        : base(
            TheName,
            "Edits a table on a slide: set cells (text, style, fill, merge with column_span/row_span), insert or delete rows and columns, replace every row, change column widths, or restyle it (header colours, banding, borders, fonts, total row). tabular_sql replaces the rows with a query over the conversation's spreadsheet data. Rows and columns count from 1, header included. The table is found by 'element', or is the only table on the slide.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "slide": { "type": "integer" },
                "element": { "type": ["integer", "string"], "description": "The table's #id or name. Optional when the slide has one table." },
                "rows": { "type": "array", "items": { "type": "array", "items": { "type": "string" } }, "description": "Replace every row, header first." },
                "cells": { "type": "array", "items": { "type": "object", "properties": {
                  "row": { "type": "integer" }, "column": { "type": "integer" }, "text": { "type": "string" },
                  "style": TEXTSTYLE, "fill": COLOR,
                  "column_span": { "type": "integer" }, "row_span": { "type": "integer" }
                }, "required": ["row", "column"] } },
                "insert_rows": { "type": "array", "items": { "type": "array", "items": { "type": "string" } } },
                "insert_rows_after": { "type": "integer", "description": "Insert after this row; 0 for the top. Defaults to the end." },
                "insert_columns": { "type": "array", "items": { "type": "array", "items": { "type": "string" } }, "description": "Each column's cells, top to bottom." },
                "insert_columns_after": { "type": "integer" },
                "delete_rows": { "type": "array", "items": { "type": "integer" } },
                "delete_columns": { "type": "array", "items": { "type": "integer" } },
                "column_widths": { "type": "array", "items": LENGTH },
                "header": { "type": "boolean" },
                "table_style": TABLESTYLE,
                "tabular_sql": { "type": "string", "description": "Replace the rows with this read-only query's result." },
                "max_rows": { "type": "integer" },
                "link_data": { "type": "boolean", "description": "Keep the table tied to tabular_sql so refresh_slide_data can update it." }
              },
              "required": ["slide"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Edits the table.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var deck = call.Deck();
        var model = await call.ReadAsync(deck, cancellationToken);
        var slide = call.Slide(model);
        var arguments = call.Arguments;
        var table = PresentationDataTargets.Find(model.Slides[slide - 1], arguments.String("element", "table", "element_id"), PresentationElementKind.Table);
        var reference = table.Id.ToString(CultureInfo.InvariantCulture);
        var edits = new List<PresentationEdit>();
        var notes = new List<string>();
        var sql = arguments.String("tabular_sql", "sql", "query");
        var maxRows = Math.Clamp(arguments.Int("max_rows", "limit") ?? 25, 1, 60);

        if (!string.IsNullOrWhiteSpace(sql))
        {
            var (edit, note) = await PresentationDataTargets.BuildAsync(call.Services, slide, table, sql, null, [], maxRows, cancellationToken);
            edits.Add(edit);
            notes.Add(note);
        }

        var update = new UpdateTableEdit
        {
            Slide = slide,
            Element = reference,
            ReplaceRows = Rows(arguments.Node("rows", "data")) is { Count: > 0 } rows ? rows : null,
            InsertRows = Rows(arguments.Node("insert_rows", "add_rows")),
            InsertRowsAfter = arguments.Int("insert_rows_after", "after_row"),
            InsertColumns = Rows(arguments.Node("insert_columns", "add_columns")),
            InsertColumnsAfter = arguments.Int("insert_columns_after", "after_column"),
            DeleteRows = PresentationArguments.Items(arguments.Node("delete_rows", "remove_rows")).Select(PresentationArguments.ReadInt).OfType<int>().ToList(),
            DeleteColumns = PresentationArguments.Items(arguments.Node("delete_columns", "remove_columns")).Select(PresentationArguments.ReadInt).OfType<int>().ToList(),
            ColumnWidths = PresentationArguments.Items(arguments.Node("column_widths", "widths")).Select(PresentationArguments.ReadLength).ToList(),
            HeaderRow = arguments.Bool("header", "header_row"),
            Style = PresentationArguments.ReadTableStyle(arguments.Object("table_style", "style")),
        };

        foreach (var cell in arguments.Array("cells", "cell_edits").OfType<JsonObject>())
        {
            update.Cells.Add(new PresentationTableCellEdit
            {
                Row = PresentationArguments.ReadInt(PresentationArguments.Find(cell, "row")) ?? throw new PresentationArgumentException("Each cell needs its 'row', counting from 1."),
                Column = PresentationArguments.ReadInt(PresentationArguments.Find(cell, "column", "col")) ?? throw new PresentationArgumentException("Each cell needs its 'column', counting from 1."),
                Text = PresentationArguments.ReadString(PresentationArguments.Find(cell, "text", "value")),
                TextStyle = PresentationArguments.ReadTextStyle(cell),
                Fill = PresentationArguments.ReadString(PresentationArguments.Find(cell, "fill", "background", "fill_color")),
                ColumnSpan = PresentationArguments.ReadInt(PresentationArguments.Find(cell, "column_span", "colspan", "merge_columns")),
                RowSpan = PresentationArguments.ReadInt(PresentationArguments.Find(cell, "row_span", "rowspan", "merge_rows")),
            });
        }

        if (update.ReplaceRows is not null || update.InsertRows.Count > 0 || update.InsertColumns.Count > 0 || update.DeleteRows.Count > 0 ||
            update.DeleteColumns.Count > 0 || update.ColumnWidths.Count > 0 || update.HeaderRow is not null || update.Style is not null || update.Cells.Count > 0)
        {
            edits.Add(update);
        }

        if (edits.Count == 0)
        {
            throw new PresentationArgumentException("Say what to change: cells, rows, insert_rows, delete_rows, insert_columns, delete_columns, column_widths, table_style or tabular_sql.");
        }

        var result = await call.Session.ApplyAsync(deck, edits, $"edited the table on slide {slide}", cancellationToken);

        if (!string.IsNullOrWhiteSpace(sql) && arguments.Bool("link_data", "keep_linked", "linked") == true)
        {
            PresentationDataLinks.Set(call.Services, deck, model.Slides[slide - 1].SlideId, table.Id, "table", sql, null, [], maxRows);
            await call.Session.Workspace.SaveStateAsync();
            notes.Add("The table stays linked: refresh_slide_data updates it from its query.");
        }

        return Changed(deck, result, notes);
    }

    private static List<IList<string>> Rows(JsonNode node)
    {
        return PresentationArguments.Items(node)
            .Select(row => (IList<string>)PresentationArguments.Items(row).Select(cell => PresentationArguments.ReadString(cell) ?? string.Empty).ToList())
            .ToList();
    }
}
