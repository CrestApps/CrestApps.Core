using System.Text.Json;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Changes a table in place: cell text, rows, columns and the repeating header row.
/// </summary>
internal sealed class UpdateWordTableTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.UpdateWordTable;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "table": { "type": "string", "description": "The table's id, the id of a paragraph in it, or its number in the document (1 = first). Optional when there is one table." },
            "cells": {
              "type": "array",
              "description": "Cells to set. Columns count from 1. The changes are made in this order: remove_rows, remove_columns, add_column, add_rows, cells, so a row or column number counts the table as the earlier changes left it; name rows by their first cell's text to avoid that.",
              "items": {
                "type": "object",
                "properties": {
                  "row": { "type": ["integer", "string"], "description": "The text of the row's first cell, such as \"Testing\", or its number counting the header row as 1." },
                  "column": { "type": "integer" },
                  "text": { "type": "string", "description": "Inline Markdown, written as given: write numbers the way the column shows them, such as $9,500.00." }
                },
                "required": ["row", "column", "text"]
              }
            },
            "add_rows": { "type": "array", "items": { "type": "array", "items": { "type": ["string", "number", "boolean", "null"] } }, "description": "Rows to add, one array of cell values each. They copy the look of the row they follow." },
            "after_row": { "type": ["integer", "string"], "description": "Add the rows after this row: the text of its first cell, or its number. Default: after the last row." },
            "remove_rows": { "type": "array", "items": { "type": ["integer", "string"] }, "description": "Rows by the text of their first cell, or their number counting the header row as 1." },
            "add_column": {
              "type": "object",
              "properties": { "header": { "type": "string" }, "values": { "type": "array", "items": { "type": ["string", "number", "boolean", "null"] } }, "after_column": { "type": "integer", "description": "Default: after the last column." } },
              "required": ["header"]
            },
            "remove_columns": { "type": "array", "items": { "type": "integer" } },
            "repeat_header": { "type": "boolean", "description": "Repeat the first row at the top of every page the table runs onto." },
            {{WordToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateWordTableTool"/> class.
    /// </summary>
    public UpdateWordTableTool()
        : base(Schema)
    {
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Changes a table of a Word document in place: set 'cells' by row (the text of its first cell, such as \"Testing\") and column, 'add_rows' (they copy the look of the row before), 'remove_rows', 'add_column', 'remove_columns', and 'repeat_header' across pages. Cell text changes are tracked when tracking is on; row and column changes are not. To restyle the table's text use format_word_content with the table's id.";

    /// <summary>
    /// Changes the table.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var (summary, document) = await context.EditAsync(arguments.Document(), "Changed a table", edit =>
        {
            var table = WordBlockLocator.RequireTable(edit.Package, arguments.GetString("table"));
            var part = edit.Package.MainPart;
            var revisions = WordRevisions.IsTracking(edit.Package) ? WordRevisions.For(edit.Package, edit.Author, edit.Now) : null;
            var changes = new List<string>();

            if (arguments.TryGetElement("remove_rows", out var removeRows) && removeRows.ValueKind == JsonValueKind.Array)
            {
                var rows = table.Elements<TableRow>().ToList();
                var targets = removeRows.EnumerateArray().Select(value => Row(rows, value)).Distinct().ToList();

                if (targets.Count >= rows.Count)
                {
                    throw new WordToolException("That would remove every row; remove the table with remove_word_content instead.");
                }

                foreach (var row in targets)
                {
                    row.Remove();
                }

                changes.Add($"removed {targets.Count} row(s)");
            }

            if (arguments.TryGetElement("remove_columns", out var removeColumns) && removeColumns.ValueKind == JsonValueKind.Array)
            {
                var numbers = removeColumns.EnumerateArray().Select(WordJsonValues.ReadDouble).Where(value => value is not null).Select(value => (int)value.Value).Distinct().OrderDescending().ToList();
                var grid = table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().ToList() ?? [];

                if (numbers.Count >= grid.Count)
                {
                    throw new WordToolException("That would remove every column; remove the table with remove_word_content instead.");
                }

                foreach (var number in numbers)
                {
                    foreach (var row in table.Elements<TableRow>())
                    {
                        Cell(row, number).Remove();
                    }

                    Column(grid, number).Remove();
                }

                Fit(table);
                changes.Add($"removed {numbers.Count} column(s)");
            }

            if (arguments.TryGetObject("add_column", out var column))
            {
                var grid = table.GetFirstChild<TableGrid>().Elements<GridColumn>().ToList();
                var after = Math.Clamp(WordJsonValues.GetInt(column, "after_column") ?? grid.Count, 1, grid.Count);
                var values = WordJsonValues.TryGet(column, "values", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(Text).ToList() : [];
                var index = 0;

                foreach (var row in table.Elements<TableRow>())
                {
                    var copy = Blank((TableCell)Cell(row, after).CloneNode(true));
                    var text = index == 0 ? WordJsonValues.GetRawString(column, "header") : index - 1 < values.Count ? values[index - 1] : string.Empty;

                    SetText(copy, text, part, null);
                    edit.Package.Ids.Assign(copy);
                    Cell(row, after).InsertAfterSelf(copy);
                    index++;
                }

                Column(grid, after).InsertAfterSelf(new GridColumn());
                Fit(table);
                changes.Add($"added the column \"{WordJsonValues.GetRawString(column, "header")}\"");
            }

            if (arguments.TryGetElement("add_rows", out var addRows) && addRows.ValueKind == JsonValueKind.Array)
            {
                var rows = table.Elements<TableRow>().ToList();
                var anchor = arguments.TryGetElement("after_row", out var afterRow) ? Row(rows, afterRow) : rows[^1];
                var added = 0;

                foreach (var values in addRows.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Array))
                {
                    var row = Blank((TableRow)anchor.CloneNode(true));
                    var cells = row.Elements<TableCell>().ToList();
                    var texts = values.EnumerateArray().Select(Text).ToList();

                    // A copy of the header row would repeat as a header; only the first row is one.
                    row.TableRowProperties?.RemoveAllChildren<TableHeader>();

                    for (var cell = 0; cell < cells.Count; cell++)
                    {
                        SetText(cells[cell], cell < texts.Count ? texts[cell] : string.Empty, part, null);
                    }

                    edit.Package.Ids.Assign(row);
                    anchor.InsertAfterSelf(row);
                    anchor = row;
                    added++;
                }

                changes.Add($"added {added} row(s)");
            }

            if (arguments.TryGetElement("cells", out var cellList) && cellList.ValueKind == JsonValueKind.Array)
            {
                var rows = table.Elements<TableRow>().ToList();
                var count = 0;

                foreach (var item in cellList.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
                {
                    var row = WordJsonValues.TryGet(item, "row", out var rowValue) ? Row(rows, rowValue) : throw new WordToolException("Each cell needs a 'row'.");

                    SetText(Cell(row, WordJsonValues.GetInt(item, "column") ?? 0), WordJsonValues.GetRawString(item, "text") ?? string.Empty, part, revisions);
                    count++;
                }

                changes.Add($"set {count} cell(s)");
            }

            if (arguments.GetBoolean("repeat_header") is { } repeat)
            {
                var first = table.Elements<TableRow>().First();

                if (repeat)
                {
                    (first.TableRowProperties ??= new TableRowProperties()).Append(new TableHeader());
                }
                else
                {
                    first.TableRowProperties?.RemoveAllChildren<TableHeader>();
                }

                changes.Add(repeat ? "the header row repeats on every page" : "the header row no longer repeats");
            }

            if (changes.Count == 0)
            {
                throw new WordToolException("Say what to change: 'cells', 'add_rows', 'remove_rows', 'add_column', 'remove_columns' or 'repeat_header'.");
            }

            var size = $"{table.Elements<TableRow>().Count()} rows × {table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().Count()} columns";

            return Task.FromResult($"{string.Join("; ", changes)}. The table [{WordParagraphIds.Of(table)}] has {size}");
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary}.";
    }

    // A copied row or cell keeps only its look: comments, bookmarks and note references belong to the original,
    // and a second copy of them would make the document invalid.
    private static T Blank<T>(T element)
        where T : OpenXmlElement
    {
        foreach (var marker in element.Descendants().Where(item => item is BookmarkStart or BookmarkEnd or CommentRangeStart or CommentRangeEnd).ToList())
        {
            marker.Remove();
        }

        foreach (var run in element.Descendants<Run>().Where(run => run.ChildElements.Any(child => child is CommentReference or FootnoteReference or EndnoteReference)).ToList())
        {
            run.Remove();
        }

        return element;
    }

    private static void SetText(TableCell cell, string markdown, OpenXmlPart part, WordRevisions revisions)
    {
        var paragraphs = cell.Elements<Paragraph>().ToList();

        if (paragraphs.Count == 0)
        {
            paragraphs.Add(cell.AppendChild(new Paragraph()));
        }

        WordTextEditor.ReplaceParagraph(paragraphs[0], markdown, part, revisions);

        foreach (var extra in paragraphs.Skip(1))
        {
            if (revisions is null)
            {
                extra.Remove();
            }
            else
            {
                revisions.MarkDeleted(extra);
            }
        }
    }

    // A table's columns are laid out from its grid; after a column is added or removed every column gets an
    // equal share of the table's width, and the cells follow.
    private static void Fit(Table table)
    {
        var grid = table.GetFirstChild<TableGrid>().Elements<GridColumn>().ToList();
        var total = grid.Sum(item => int.TryParse(item.Width?.Value, out var width) ? width : 0);

        if (total <= 0)
        {
            total = 9360;
        }

        var share = total / grid.Count;

        foreach (var item in grid)
        {
            item.Width = share.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var cell in table.Descendants<TableCell>())
        {
            var properties = cell.TableCellProperties ??= new TableCellProperties();
            var span = properties.GridSpan?.Val?.Value ?? 1;

            properties.TableCellWidth = new TableCellWidth { Width = (share * span).ToString(System.Globalization.CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa };
        }
    }

    private static TableRow Row(List<TableRow> rows, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && !int.TryParse(value.GetString(), out _))
        {
            var label = value.GetString().Trim();

            return rows.FirstOrDefault(row => row.Elements<TableCell>().FirstOrDefault() is { } cell && string.Equals(WordText.OfCell(cell).Trim(), label, StringComparison.OrdinalIgnoreCase))
                ?? throw new WordToolException($"No row starts with \"{label}\". The rows start with: {string.Join(", ", rows.Select(row => row.Elements<TableCell>().FirstOrDefault() is { } cell ? "\"" + WordText.OfCell(cell) + "\"" : "(empty)"))}.");
        }

        return Row(rows, (int)(WordJsonValues.ReadDouble(value) ?? 0));
    }

    private static TableRow Row(List<TableRow> rows, int number)
    {
        return number >= 1 && number <= rows.Count ? rows[number - 1] : throw new WordToolException($"There is no row {number}; the table has {rows.Count}.");
    }

    private static TableCell Cell(TableRow row, int number)
    {
        var cells = row.Elements<TableCell>().ToList();

        return number >= 1 && number <= cells.Count ? cells[number - 1] : throw new WordToolException($"There is no column {number}; the row has {cells.Count} cells.");
    }

    private static GridColumn Column(List<GridColumn> grid, int number)
    {
        return number >= 1 && number <= grid.Count ? grid[number - 1] : throw new WordToolException($"There is no column {number}; the table has {grid.Count}.");
    }

    private static string Text(JsonElement value)
    {
        return WordJsonValues.ReadScalar(value) switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            var other => other.ToString(),
        };
    }
}
