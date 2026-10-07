using System.Globalization;
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
/// Changes a table in place: cell text, rows, columns, merged cells, the repeating header row and the table's
/// look.
/// </summary>
/// <remarks>
/// Columns are always the table's grid columns, counted from 1, whatever cells are merged: a cell that spans
/// several columns is found from any of them, and a column added or removed changes the grid and every row
/// the same way, so a row with merged cells keeps lining up with the others.
/// </remarks>
internal sealed class UpdateWordTableTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.UpdateWordTable;

    private const int DefaultTableWidthTwips = 9360;

    private const string Row = """{ "type": ["integer", "string"], "description": "The text of the row's first cell, such as \"Testing\", or its number counting the header row as 1." }""";

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "table": { "type": "string", "description": "The table's id, the id of a paragraph in it, or its number in the document (1 = first). Optional when there is one table." },
            "cells": {
              "type": "array",
              "description": "Cells to set. Columns are the table's columns counted from 1, merged cells included: a merged cell is set through any row and column it covers. The changes are made in this order: remove_rows, remove_columns, add_column, add_rows, split_cells, merge_cells, cells, format_cells, then the table's look, so a row or column number counts the table as the earlier changes left it; name rows by their first cell's text to avoid that.",
              "items": {
                "type": "object",
                "properties": {
                  "row": {{Row}},
                  "column": { "type": "integer" },
                  "text": { "type": "string", "description": "Inline Markdown, written as given: write numbers the way the column shows them, such as $9,500.00." }
                },
                "required": ["row", "column", "text"]
              }
            },
            "add_rows": { "type": "array", "items": { "type": "array", "items": { "type": ["string", "number", "boolean", "null"] } }, "description": "Rows to add, one array of cell values each, one value per column. They copy the look of the row they follow, without its merged cells." },
            "after_row": { "type": ["integer", "string"], "description": "Add the rows after this row: the text of its first cell, or its number. Default: after the last row." },
            "remove_rows": { "type": "array", "items": { "type": ["integer", "string"] }, "description": "Rows by the text of their first cell, or their number counting the header row as 1." },
            "add_column": {
              "type": "object",
              "properties": { "header": { "type": "string" }, "values": { "type": "array", "items": { "type": ["string", "number", "boolean", "null"] } }, "after_column": { "type": "integer", "description": "Default: after the last column. 0 puts it first." } },
              "required": ["header"]
            },
            "remove_columns": { "type": "array", "items": { "type": "integer" }, "description": "Columns to remove. A merged cell that spans a removed column gets narrower instead." },
            "merge_cells": {
              "type": "array",
              "description": "Rectangles of cells to merge into one: from 'row' and 'column' to 'to_row' and 'to_column' (each defaults to the start). The text of every merged cell is kept, one paragraph after another.",
              "items": {
                "type": "object",
                "properties": {
                  "row": {{Row}},
                  "column": { "type": "integer" },
                  "to_row": { "type": ["integer", "string"] },
                  "to_column": { "type": "integer" }
                },
                "required": ["row", "column"]
              }
            },
            "split_cells": {
              "type": "array",
              "description": "Cells to split, named by any row and column they cover. A merged cell splits back into the cells it covers, its text staying in the first; any other cell splits into 'columns' side-by-side cells.",
              "items": {
                "type": "object",
                "properties": {
                  "row": {{Row}},
                  "column": { "type": "integer" },
                  "columns": { "type": "integer", "description": "For a cell that is not merged: how many cells to split it into. Default 2." }
                },
                "required": ["row", "column"]
              }
            },
            "format_cells": {
              "type": "array",
              "description": "Cell formatting for one cell or a rectangle up to 'to_row' and 'to_column'. To restyle the text in cells use format_word_content.",
              "items": {
                "type": "object",
                "properties": {
                  "row": {{Row}},
                  "column": { "type": "integer" },
                  "to_row": { "type": ["integer", "string"] },
                  "to_column": { "type": "integer" },
                  "fill": { "type": "string", "description": "Background color, or none." },
                  "alignment": { "type": "string", "enum": ["left", "center", "right", "justify"] },
                  "vertical_alignment": { "type": "string", "enum": ["top", "center", "bottom"] }
                },
                "required": ["row", "column"]
              }
            },
            "column_widths": { "type": "array", "items": { "type": ["number", "string"] }, "description": "Widths of the columns from the first: a length such as \"1.5in\" or \"3cm\", a percentage of the table's width such as \"30%\", or a number as a relative weight sharing what the lengths leave. Columns not listed keep their width." },
            "table_style": { "type": "string", "description": "data (shaded header, banded rows), grid, light (horizontal rules), plain (no borders), or a document table style name." },
            "banded": { "type": "boolean", "description": "Shade every other row, when the table style has banding." },
            "border_color": { "type": "string", "description": "Draw every border in this color." },
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
    public override string Description => "Changes a table of a Word document in place: set 'cells' by row (the text of its first cell, such as \"Testing\") and column, 'add_rows' (they copy the look of the row before), 'remove_rows', 'add_column', 'remove_columns', 'merge_cells' (a rectangle of cells becomes one, keeping their text), 'split_cells' (a merged cell splits back, any other cell into several), 'format_cells' (fill, alignment, vertical alignment), 'column_widths', 'table_style', 'banded', 'border_color', and 'repeat_header' across pages. Columns count the table's columns, merged cells included. Cell text changes are tracked when tracking is on; the table's structure and look are not. To restyle the table's text use format_word_content with the table's id.";

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
            var grid = EnsureGrid(table);
            var tableWidth = grid.Elements<GridColumn>().Sum(Width);
            var resized = false;

            if (arguments.TryGetElement("remove_rows", out var removeRows) && removeRows.ValueKind == JsonValueKind.Array)
            {
                var rows = Rows(table);
                var targets = removeRows.EnumerateArray().Select(value => FindRow(rows, value)).Distinct().ToList();

                if (targets.Count >= rows.Count)
                {
                    throw new WordToolException("That would remove every row; remove the table with remove_word_content instead.");
                }

                foreach (var row in targets)
                {
                    RemoveRow(table, row);
                }

                changes.Add($"removed {targets.Count} row(s)");
            }

            if (arguments.TryGetElement("remove_columns", out var removeColumns) && removeColumns.ValueKind == JsonValueKind.Array)
            {
                var columns = grid.Elements<GridColumn>().ToList();
                var numbers = removeColumns.EnumerateArray().Select(WordJsonValues.ReadDouble).Where(value => value is not null).Select(value => (int)value.Value).Distinct().OrderDescending().ToList();

                if (numbers.Count >= columns.Count)
                {
                    throw new WordToolException("That would remove every column; remove the table with remove_word_content instead.");
                }

                foreach (var number in numbers)
                {
                    RequireColumn(columns.Count, number);
                }

                foreach (var number in numbers)
                {
                    RemoveColumn(table, number);
                    columns[number - 1].Remove();
                }

                resized = true;
                changes.Add($"removed {numbers.Count} column(s)");
            }

            if (arguments.TryGetObject("add_column", out var column))
            {
                var columns = grid.Elements<GridColumn>().ToList();
                var after = Math.Clamp(WordJsonValues.GetInt(column, "after_column") ?? columns.Count, 0, columns.Count);
                var values = WordJsonValues.TryGet(column, "values", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(Text).ToList() : [];
                var header = WordJsonValues.GetRawString(column, "header");
                var texts = new List<string> { header };

                texts.AddRange(values);
                AddColumn(table, after, texts, part, edit.Package.Ids);

                var added = new GridColumn { Width = Invariant(Width(columns[Math.Max(after, 1) - 1])) };

                if (after == 0)
                {
                    columns[0].InsertBeforeSelf(added);
                }
                else
                {
                    columns[after - 1].InsertAfterSelf(added);
                }

                resized = true;
                changes.Add($"added the column \"{header}\"");
            }

            if (arguments.TryGetElement("add_rows", out var addRows) && addRows.ValueKind == JsonValueKind.Array)
            {
                var rows = Rows(table);
                var anchor = arguments.TryGetElement("after_row", out var afterRow) ? FindRow(rows, afterRow) : rows[^1];
                var added = 0;

                foreach (var values in addRows.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Array))
                {
                    var row = Blank((TableRow)anchor.CloneNode(true));
                    var texts = values.EnumerateArray().Select(Text).ToList();

                    // A copy of the header row would repeat as a header; only the first row is one.
                    row.TableRowProperties?.RemoveAllChildren<TableHeader>();

                    // A new row takes one value per column, so the copy keeps none of the merged cells it was made from.
                    Unmerge(row);

                    var cells = row.Elements<TableCell>().ToList();

                    for (var cell = 0; cell < cells.Count; cell++)
                    {
                        SetText(cells[cell], cell < texts.Count ? texts[cell] : string.Empty, part, null);
                    }

                    edit.Package.Ids.Assign(row);
                    anchor.InsertAfterSelf(row);
                    anchor = row;
                    added++;
                }

                resized = true;
                changes.Add($"added {added} row(s)");
            }

            if (arguments.TryGetElement("split_cells", out var splitCells) && splitCells.ValueKind == JsonValueKind.Array)
            {
                var count = 0;

                foreach (var item in splitCells.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
                {
                    count += SplitCell(table, item, edit.Package.Ids);
                }

                resized = true;
                changes.Add($"split {count} cell(s)");
            }

            if (arguments.TryGetElement("merge_cells", out var mergeCells) && mergeCells.ValueKind == JsonValueKind.Array)
            {
                var count = 0;

                foreach (var item in mergeCells.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
                {
                    MergeCells(table, item);
                    count++;
                }

                resized = true;
                changes.Add($"merged {count} range(s) of cells");
            }

            if (arguments.TryGetElement("cells", out var cellList) && cellList.ValueKind == JsonValueKind.Array)
            {
                var rows = Rows(table);
                var count = 0;

                foreach (var item in cellList.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
                {
                    var row = WordJsonValues.TryGet(item, "row", out var rowValue) ? FindRow(rows, rowValue) : throw new WordToolException("Each cell needs a 'row'.");

                    SetText(VisibleCell(table, rows, rows.IndexOf(row), WordJsonValues.GetInt(item, "column") ?? 0), WordJsonValues.GetRawString(item, "text") ?? string.Empty, part, revisions);
                    count++;
                }

                changes.Add($"set {count} cell(s)");
            }

            if (arguments.TryGetElement("format_cells", out var formatCells) && formatCells.ValueKind == JsonValueKind.Array)
            {
                var count = 0;

                foreach (var item in formatCells.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
                {
                    count += FormatCells(table, item);
                }

                changes.Add($"formatted {count} cell(s)");
            }

            if (arguments.TryGetElement("column_widths", out var widths) && widths.ValueKind == JsonValueKind.Array)
            {
                tableWidth = SetColumnWidths(table, [.. widths.EnumerateArray()], tableWidth);
                resized = true;
                changes.Add("set the column widths");
            }

            if (arguments.GetString("table_style") is { } style)
            {
                SetStyle(table, style, arguments.GetString("border_color"), part, edit.Design);
                changes.Add($"applied the table style \"{style}\"");
            }
            else if (arguments.GetString("border_color") is { } borderColor)
            {
                var properties = table.GetFirstChild<TableProperties>() ?? table.PrependChild(new TableProperties());

                properties.TableBorders = Borders(ColorOf(borderColor), verticals: true, outerSides: true);
                changes.Add("changed the border color");
            }

            if (arguments.GetBoolean("banded") is { } banded)
            {
                SetBanded(table, banded);
                changes.Add(banded ? "every other row is shaded" : "rows are no longer banded");
            }

            if (arguments.GetBoolean("repeat_header") is { } repeat)
            {
                var first = table.Elements<TableRow>().First();

                first.TableRowProperties?.RemoveAllChildren<TableHeader>();

                if (repeat)
                {
                    (first.TableRowProperties ??= new TableRowProperties()).Append(new TableHeader());
                }

                changes.Add(repeat ? "the header row repeats on every page" : "the header row no longer repeats");
            }

            if (changes.Count == 0)
            {
                throw new WordToolException("Say what to change: 'cells', 'add_rows', 'remove_rows', 'add_column', 'remove_columns', 'merge_cells', 'split_cells', 'format_cells', 'column_widths', 'table_style', 'banded', 'border_color' or 'repeat_header'.");
            }

            NormalizeVerticalMerges(table);

            if (resized)
            {
                Fit(table, tableWidth);
            }

            var size = $"{table.Elements<TableRow>().Count()} rows × {grid.Elements<GridColumn>().Count()} columns";

            return Task.FromResult($"{string.Join("; ", changes)}. The table [{WordParagraphIds.Of(table)}] has {size}");
        }, arguments.SaveAs(), cancellationToken);

        return $"\"{document.Name}\" (version {document.Version}): {summary}.";
    }

    // A cell's place in its row: the first grid column it covers, counted from 1, and how many it covers.
    private readonly record struct Slot(TableCell Cell, int Start, int Span)
    {
        public int End => Start + Span - 1;

        public bool Covers(int column) => column >= Start && column <= End;
    }

    private static List<TableRow> Rows(Table table)
    {
        return [.. table.Elements<TableRow>()];
    }

    private static List<Slot> Slots(TableRow row)
    {
        var slots = new List<Slot>();
        var column = 1 + GridBefore(row);

        foreach (var cell in row.Elements<TableCell>())
        {
            var span = Span(cell);

            slots.Add(new Slot(cell, column, span));
            column += span;
        }

        return slots;
    }

    private static Slot? SlotAt(TableRow row, int column)
    {
        foreach (var slot in Slots(row))
        {
            if (slot.Covers(column))
            {
                return slot;
            }
        }

        return null;
    }

    private static int Span(TableCell cell)
    {
        return Math.Max(1, cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1);
    }

    private static void SetSpan(TableCell cell, int span)
    {
        if (span > 1)
        {
            (cell.TableCellProperties ??= new TableCellProperties()).GridSpan = new GridSpan { Val = span };
        }
        else if (cell.TableCellProperties is { } properties)
        {
            properties.GridSpan = null;
        }
    }

    private static int GridBefore(TableRow row)
    {
        return Math.Max(0, row.TableRowProperties?.GetFirstChild<GridBefore>()?.Val?.Value ?? 0);
    }

    private static int GridAfter(TableRow row)
    {
        return Math.Max(0, row.TableRowProperties?.GetFirstChild<GridAfter>()?.Val?.Value ?? 0);
    }

    private static void SetGridBefore(TableRow row, int value)
    {
        row.TableRowProperties?.RemoveAllChildren<GridBefore>();

        if (value > 0)
        {
            (row.TableRowProperties ??= new TableRowProperties()).Append(new GridBefore { Val = value });
        }
    }

    private static void SetGridAfter(TableRow row, int value)
    {
        row.TableRowProperties?.RemoveAllChildren<GridAfter>();

        if (value > 0)
        {
            (row.TableRowProperties ??= new TableRowProperties()).Append(new GridAfter { Val = value });
        }
    }

    private static bool IsContinuation(TableCell cell)
    {
        return cell.TableCellProperties?.VerticalMerge is { } merge && (merge.Val is null || merge.Val.Value == MergedCellValues.Continue);
    }

    private static bool IsRestart(TableCell cell)
    {
        return cell.TableCellProperties?.VerticalMerge?.Val?.Value == MergedCellValues.Restart;
    }

    private static void SetVerticalMerge(TableCell cell, MergedCellValues? value)
    {
        if (value is null)
        {
            if (cell.TableCellProperties is { } properties)
            {
                properties.VerticalMerge = null;
            }

            return;
        }

        (cell.TableCellProperties ??= new TableCellProperties()).VerticalMerge = value == MergedCellValues.Restart
            ? new VerticalMerge { Val = MergedCellValues.Restart }
            : new VerticalMerge();
    }

    // The cell of the row below that continues a vertically merged cell, or null.
    private static Slot? ContinuationBelow(List<TableRow> rows, int index, Slot slot)
    {
        return index + 1 < rows.Count && SlotAt(rows[index + 1], slot.Start) is { } below && below.Start == slot.Start && below.Span == slot.Span && IsContinuation(below.Cell)
            ? below
            : null;
    }

    private static TableGrid EnsureGrid(Table table)
    {
        var grid = table.GetFirstChild<TableGrid>();

        if (grid is not null && grid.Elements<GridColumn>().Any())
        {
            return grid;
        }

        // A table written without a grid gets one from its widest row, so columns can be counted.
        var count = Math.Max(1, table.Elements<TableRow>().Select(row => GridBefore(row) + row.Elements<TableCell>().Sum(Span) + GridAfter(row)).DefaultIfEmpty(1).Max());

        grid ??= table.GetFirstChild<TableProperties>() is { } properties ? properties.InsertAfterSelf(new TableGrid()) : table.PrependChild(new TableGrid());

        for (var index = 0; index < count; index++)
        {
            grid.Append(new GridColumn { Width = Invariant(DefaultTableWidthTwips / count) });
        }

        return grid;
    }

    private static int ColumnCount(Table table)
    {
        return table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().Count() ?? 0;
    }

    private static void RequireColumn(int count, int number)
    {
        if (number < 1 || number > count)
        {
            throw new WordToolException($"There is no column {number}; the table has {count}.");
        }
    }

    private static int Width(GridColumn column)
    {
        return int.TryParse(column.Width?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ? Math.Max(0, width) : 0;
    }

    // Removing a row that starts a vertically merged cell hands the merged cell, and its text, to the row below.
    private static void RemoveRow(Table table, TableRow row)
    {
        var rows = Rows(table);
        var index = rows.IndexOf(row);

        foreach (var slot in Slots(row).Where(slot => IsRestart(slot.Cell)))
        {
            if (ContinuationBelow(rows, index, slot) is { } below)
            {
                MoveContent(slot.Cell, below.Cell, replace: true);
                SetVerticalMerge(below.Cell, MergedCellValues.Restart);
            }
        }

        row.Remove();
    }

    // A removed column takes the cell in it away, or narrows a merged cell that spans it.
    private static void RemoveColumn(Table table, int number)
    {
        var rowNumber = 0;

        foreach (var row in Rows(table))
        {
            rowNumber++;

            var before = GridBefore(row);

            if (number <= before)
            {
                SetGridBefore(row, before - 1);

                continue;
            }

            if (SlotAt(row, number) is not { } slot)
            {
                var last = Slots(row).Select(item => item.End).DefaultIfEmpty(before).Max();
                var after = GridAfter(row);

                if (number > last && number <= last + after)
                {
                    SetGridAfter(row, after - 1);
                }

                continue;
            }

            if (slot.Span > 1)
            {
                SetSpan(slot.Cell, slot.Span - 1);

                continue;
            }

            if (row.Elements<TableCell>().Count() == 1)
            {
                throw new WordToolException($"Removing column {number} would leave row {rowNumber} without cells; remove the row instead, or merge its cell across more columns first.");
            }

            slot.Cell.Remove();
        }
    }

    // A column is added to every row the same way: a new cell after the cell that ends at the column it
    // follows, or a wider merged cell when the new column falls inside one.
    private static void AddColumn(Table table, int after, List<string> texts, OpenXmlPart part, WordParagraphIds ids)
    {
        var index = 0;

        foreach (var row in Rows(table))
        {
            var text = index < texts.Count ? texts[index] ?? string.Empty : string.Empty;
            var before = GridBefore(row);

            index++;

            if (before > 0 && after <= before)
            {
                SetGridBefore(row, before + 1);

                continue;
            }

            if (after == 0)
            {
                var first = row.Elements<TableCell>().First();
                var copy = NewCell(first, part, text);

                ids.Assign(copy);
                first.InsertBeforeSelf(copy);

                continue;
            }

            if (SlotAt(row, after) is not { } slot)
            {
                SetGridAfter(row, GridAfter(row) + 1);

                continue;
            }

            if (after < slot.End)
            {
                SetSpan(slot.Cell, slot.Span + 1);

                continue;
            }

            var cell = NewCell(slot.Cell, part, text);

            ids.Assign(cell);
            slot.Cell.InsertAfterSelf(cell);
        }
    }

    // A new cell with the look of another, unmerged and holding the given text.
    private static TableCell NewCell(TableCell model, OpenXmlPart part, string text)
    {
        var cell = Blank((TableCell)model.CloneNode(true));

        SetSpan(cell, 1);
        SetVerticalMerge(cell, null);

        if (cell.TableCellProperties is { } properties)
        {
            properties.HorizontalMerge = null;
        }

        // Only the first paragraph's look is kept: a nested table or a content control belongs to the original.
        foreach (var child in cell.ChildElements.Where(child => child is not (TableCellProperties or Paragraph)).ToList())
        {
            child.Remove();
        }

        SetText(cell, text, part, null);

        return cell;
    }

    // Splits every merged cell of a row back into one cell per column, the extra cells empty.
    private static void Unmerge(TableRow row)
    {
        foreach (var cell in row.Elements<TableCell>().ToList())
        {
            var span = Span(cell);

            SetVerticalMerge(cell, null);
            SetSpan(cell, 1);

            for (var extra = 1; extra < span; extra++)
            {
                cell.InsertAfterSelf(EmptyCopy(cell));
            }
        }
    }

    // An empty cell with the look of another: its cell properties, without any merge, and an empty paragraph.
    private static TableCell EmptyCopy(TableCell model)
    {
        var properties = (TableCellProperties)model.TableCellProperties?.CloneNode(true) ?? new TableCellProperties();

        properties.GridSpan = null;
        properties.VerticalMerge = null;
        properties.HorizontalMerge = null;

        var paragraph = new Paragraph();

        if (model.Elements<Paragraph>().FirstOrDefault()?.ParagraphProperties is { } paragraphProperties)
        {
            paragraph.ParagraphProperties = (ParagraphProperties)paragraphProperties.CloneNode(true);
        }

        return new TableCell(properties, paragraph);
    }

    private static int SplitCell(Table table, JsonElement item, WordParagraphIds ids)
    {
        var rows = Rows(table);
        var rowIndex = rows.IndexOf(WordJsonValues.TryGet(item, "row", out var rowValue) ? FindRow(rows, rowValue) : throw new WordToolException("Each cell to split needs a 'row'."));
        var column = WordJsonValues.GetInt(item, "column") ?? 0;
        var count = ColumnCount(table);

        RequireColumn(count, column);

        var slot = SlotAt(rows[rowIndex], column) ?? throw new WordToolException($"Row {rowIndex + 1} has no cell in column {column}.");

        if (slot.Span > 1 || slot.Cell.TableCellProperties?.VerticalMerge is not null)
        {
            // A merged cell splits back into the cells it covers, from the row it starts in to the last row it
            // continues into.
            var top = rowIndex;

            while (top > 0 && IsContinuation(SlotAt(rows[top], slot.Start).Value.Cell) &&
                SlotAt(rows[top - 1], slot.Start) is { } above && above.Start == slot.Start && above.Span == slot.Span && above.Cell.TableCellProperties?.VerticalMerge is not null)
            {
                top--;
            }

            var bottom = top;

            while (ContinuationBelow(rows, bottom, SlotAt(rows[bottom], slot.Start).Value) is not null)
            {
                bottom++;
            }

            for (var index = top; index <= bottom; index++)
            {
                var cell = SlotAt(rows[index], slot.Start).Value.Cell;

                SetVerticalMerge(cell, null);
                SetSpan(cell, 1);

                var anchor = cell;

                for (var extra = 1; extra < slot.Span; extra++)
                {
                    var copy = EmptyCopy(cell);

                    ids.Assign(copy);
                    anchor.InsertAfterSelf(copy);
                    anchor = copy;
                }
            }

            return 1;
        }

        var parts = WordJsonValues.GetInt(item, "columns") ?? 2;

        if (parts < 2 || parts > 20)
        {
            throw new WordToolException("'columns' must be between 2 and 20: the number of cells to split the cell into.");
        }

        // A cell that is not merged becomes several: its grid column is divided, and every other row's cell in
        // that column spans the new columns, so the rest of the table looks the same.
        var grid = table.GetFirstChild<TableGrid>().Elements<GridColumn>().ToList();
        var width = Width(grid[column - 1]);
        var share = Math.Max(1, width / parts);

        grid[column - 1].Width = Invariant(share);

        for (var extra = 1; extra < parts; extra++)
        {
            grid[column - 1].InsertAfterSelf(new GridColumn { Width = Invariant(share) });
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];

            if (index == rowIndex)
            {
                var anchor = slot.Cell;

                for (var extra = 1; extra < parts; extra++)
                {
                    var copy = EmptyCopy(slot.Cell);

                    ids.Assign(copy);
                    anchor.InsertAfterSelf(copy);
                    anchor = copy;
                }

                continue;
            }

            var before = GridBefore(row);

            if (column <= before)
            {
                SetGridBefore(row, before + parts - 1);
            }
            else if (SlotAt(row, column) is { } other)
            {
                SetSpan(other.Cell, other.Span + parts - 1);
            }
            else if (GridAfter(row) > 0)
            {
                SetGridAfter(row, GridAfter(row) + parts - 1);
            }
        }

        return 1;
    }

    private static void MergeCells(Table table, JsonElement item)
    {
        var rows = Rows(table);
        var (firstRow, lastRow, firstColumn, lastColumn) = Range(table, rows, item);

        if (firstRow == lastRow && firstColumn == lastColumn)
        {
            throw new WordToolException("A merge needs at least two cells: give 'to_row' or 'to_column'.");
        }

        var range = new List<List<Slot>>();

        for (var index = firstRow; index <= lastRow; index++)
        {
            var first = SlotAt(rows[index], firstColumn);
            var last = SlotAt(rows[index], lastColumn);

            if (first is null || last is null)
            {
                throw new WordToolException($"Row {index + 1} has no cell in column {(first is null ? firstColumn : lastColumn)}, so the range cannot be merged.");
            }

            if (first.Value.Start != firstColumn || last.Value.End != lastColumn)
            {
                throw new WordToolException($"The range cuts through a merged cell in row {index + 1}; split it first, or widen the range to cover it.");
            }

            var slots = Slots(rows[index]).Where(slot => slot.Start >= firstColumn && slot.End <= lastColumn).ToList();

            if (index == firstRow && slots.Any(slot => IsContinuation(slot.Cell)))
            {
                throw new WordToolException($"The range cuts through a cell merged with the row above row {index + 1}; split it first, or start the range higher.");
            }

            if (index == lastRow && slots.Any(slot => ContinuationBelow(rows, index, slot) is not null))
            {
                throw new WordToolException($"The range cuts through a cell merged with the row below row {index + 1}; split it first, or end the range lower.");
            }

            range.Add(slots);
        }

        // The merged cell keeps the content of every cell it covers, in reading order.
        var target = range[0][0].Cell;
        var moved = new List<OpenXmlElement>();

        foreach (var slot in range.SelectMany(slots => slots).Where(slot => slot.Cell != target))
        {
            if (HasContent(slot.Cell))
            {
                moved.AddRange(slot.Cell.ChildElements.Where(child => child is not TableCellProperties).ToList());
            }
        }

        if (moved.Count > 0 && !HasContent(target))
        {
            foreach (var child in target.ChildElements.Where(child => child is not TableCellProperties).ToList())
            {
                child.Remove();
            }
        }

        foreach (var element in moved)
        {
            element.Remove();
            target.Append(element);
        }

        EndWithParagraph(target);

        var span = lastColumn - firstColumn + 1;

        for (var index = 0; index < range.Count; index++)
        {
            var keep = range[index][0].Cell;

            foreach (var slot in range[index].Skip(1))
            {
                slot.Cell.Remove();
            }

            SetSpan(keep, span);
            SetVerticalMerge(keep, range.Count == 1 ? null : index == 0 ? MergedCellValues.Restart : MergedCellValues.Continue);

            if (index > 0)
            {
                foreach (var child in keep.ChildElements.Where(child => child is not TableCellProperties).ToList())
                {
                    child.Remove();
                }

                keep.Append(new Paragraph());
            }
        }
    }

    private static int FormatCells(Table table, JsonElement item)
    {
        var rows = Rows(table);
        var (firstRow, lastRow, firstColumn, lastColumn) = Range(table, rows, item);
        var fill = WordJsonValues.GetString(item, "fill");
        var alignment = WordJsonValues.GetString(item, "alignment");
        var vertical = (WordJsonValues.GetString(item, "vertical_alignment") ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => (TableVerticalAlignmentValues?)null,
            "top" => TableVerticalAlignmentValues.Top,
            "center" or "middle" => TableVerticalAlignmentValues.Center,
            "bottom" => TableVerticalAlignmentValues.Bottom,
            var other => throw new WordToolException($"'vertical_alignment' must be top, center or bottom, not \"{other}\"."),
        };

        var justification = WordTableWriter.ReadAlignment(alignment);

        if (!string.IsNullOrWhiteSpace(alignment) && justification is null)
        {
            throw new WordToolException($"'alignment' must be left, center, right or justify, not \"{alignment}\".");
        }

        if (fill is null && vertical is null && justification is null)
        {
            throw new WordToolException("Each entry of 'format_cells' needs 'fill', 'alignment' or 'vertical_alignment'.");
        }

        var cells = new List<TableCell>();

        for (var index = firstRow; index <= lastRow; index++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                if (SlotAt(rows[index], column) is not null)
                {
                    var cell = VisibleCell(table, rows, index, column);

                    if (!cells.Contains(cell))
                    {
                        cells.Add(cell);
                    }
                }
            }
        }

        foreach (var cell in cells)
        {
            var properties = cell.TableCellProperties ??= new TableCellProperties();

            if (fill is not null)
            {
                properties.Shading = string.Equals(fill.Trim(), "none", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = ColorOf(fill) };
            }

            if (vertical is not null)
            {
                properties.TableCellVerticalAlignment = new TableCellVerticalAlignment { Val = vertical.Value };
            }

            if (justification is not null)
            {
                foreach (var paragraph in cell.Elements<Paragraph>())
                {
                    (paragraph.ParagraphProperties ??= new ParagraphProperties()).Justification = new Justification { Val = justification.Value };
                }
            }
        }

        return cells.Count;
    }

    // Reads the rectangle an entry names: from 'row' and 'column' to 'to_row' and 'to_column', as zero-based
    // row indexes and one-based grid columns, in order.
    private static (int FirstRow, int LastRow, int FirstColumn, int LastColumn) Range(Table table, List<TableRow> rows, JsonElement item)
    {
        var count = ColumnCount(table);
        var first = rows.IndexOf(WordJsonValues.TryGet(item, "row", out var rowValue) ? FindRow(rows, rowValue) : throw new WordToolException("Each entry needs a 'row'."));
        var last = WordJsonValues.TryGet(item, "to_row", out var toRow) ? rows.IndexOf(FindRow(rows, toRow)) : first;
        var firstColumn = WordJsonValues.GetInt(item, "column") ?? 0;
        var lastColumn = WordJsonValues.GetInt(item, "to_column") ?? firstColumn;

        RequireColumn(count, firstColumn);
        RequireColumn(count, lastColumn);

        return (Math.Min(first, last), Math.Max(first, last), Math.Min(firstColumn, lastColumn), Math.Max(firstColumn, lastColumn));
    }

    // The cell that shows a row and column: the cell covering it, or, for a cell that continues a vertical
    // merge, the cell the merge starts in.
    private static TableCell VisibleCell(Table table, List<TableRow> rows, int rowIndex, int column)
    {
        RequireColumn(ColumnCount(table), column);

        var slot = SlotAt(rows[rowIndex], column) ?? throw new WordToolException($"Row {rowIndex + 1} has no cell in column {column}.");

        for (var index = rowIndex; IsContinuation(slot.Cell) && index > 0; index--)
        {
            if (SlotAt(rows[index - 1], slot.Start) is not { } above || above.Start != slot.Start || above.Span != slot.Span || above.Cell.TableCellProperties?.VerticalMerge is null)
            {
                break;
            }

            slot = above;
        }

        return slot.Cell;
    }

    // Keeps vertical merges whole after rows or cells changed: a merge nothing continues is no merge, and a
    // continuation with nothing above it starts a merge of its own.
    private static void NormalizeVerticalMerges(Table table)
    {
        var rows = Rows(table);

        for (var index = 0; index < rows.Count; index++)
        {
            foreach (var slot in Slots(rows[index]).Where(slot => slot.Cell.TableCellProperties?.VerticalMerge is not null))
            {
                var continues = ContinuationBelow(rows, index, slot) is not null;

                if (IsContinuation(slot.Cell))
                {
                    var joined = index > 0 && SlotAt(rows[index - 1], slot.Start) is { } above &&
                        above.Start == slot.Start && above.Span == slot.Span && above.Cell.TableCellProperties?.VerticalMerge is not null;

                    if (!joined)
                    {
                        SetVerticalMerge(slot.Cell, continues ? MergedCellValues.Restart : null);
                    }
                }
                else if (!continues)
                {
                    SetVerticalMerge(slot.Cell, null);
                }
            }
        }
    }

    private static bool HasContent(TableCell cell)
    {
        return !string.IsNullOrWhiteSpace(WordText.OfCell(cell)) || cell.Descendants<Drawing>().Any() || cell.Elements<Table>().Any();
    }

    private static void MoveContent(TableCell from, TableCell to, bool replace)
    {
        if (replace)
        {
            foreach (var child in to.ChildElements.Where(child => child is not TableCellProperties).ToList())
            {
                child.Remove();
            }
        }

        foreach (var child in from.ChildElements.Where(child => child is not TableCellProperties).ToList())
        {
            child.Remove();
            to.Append(child);
        }

        EndWithParagraph(to);
        EndWithParagraph(from);
    }

    // A cell must end with a paragraph.
    private static void EndWithParagraph(TableCell cell)
    {
        if (cell.ChildElements.LastOrDefault(child => child is not TableCellProperties) is not Paragraph)
        {
            cell.Append(new Paragraph());
        }
    }

    // Sets the grid's widths from what the model asked for, and returns the table's new width.
    private static int SetColumnWidths(Table table, List<JsonElement> values, int tableWidth)
    {
        var grid = table.GetFirstChild<TableGrid>().Elements<GridColumn>().ToList();
        var total = tableWidth > 0 ? tableWidth : DefaultTableWidthTwips;
        var widths = grid.Select(column => (double)Width(column)).ToArray();
        var weights = new double[grid.Count];
        var fixedTotal = 0d;

        for (var index = 0; index < grid.Count; index++)
        {
            if (index >= values.Count || values[index].ValueKind is JsonValueKind.Null)
            {
                fixedTotal += widths[index];

                continue;
            }

            var value = values[index];
            var text = value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

            if (value.ValueKind == JsonValueKind.Number)
            {
                weights[index] = Math.Max(value.GetDouble(), 0.1);
            }
            else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
            {
                weights[index] = Math.Max(weight, 0.1);
            }
            else if (WordUnits.TryParseLength(text, WordUnits.FromTwips(total), out var points) && points > 0)
            {
                widths[index] = WordUnits.ToTwips(points);
                fixedTotal += widths[index];
            }
            else
            {
                throw new WordToolException($"Column width \"{text}\" is not a length such as 1.5in, a percentage such as 30%, or a relative weight.");
            }
        }

        var weightTotal = weights.Sum();
        var remaining = Math.Max(0, total - fixedTotal);

        for (var index = 0; index < grid.Count; index++)
        {
            if (weights[index] > 0)
            {
                widths[index] = weightTotal > 0 ? remaining * weights[index] / weightTotal : 0;
            }

            grid[index].Width = Invariant(Math.Max(360, (int)Math.Round(widths[index])));
        }

        var newTotal = grid.Sum(Width);

        // Explicit lengths that add up to something else set the table's own width; rounding does not.
        if (Math.Abs(newTotal - total) > 2 * grid.Count && table.GetFirstChild<TableProperties>() is { } properties)
        {
            properties.TableWidth = new TableWidth { Width = Invariant(newTotal), Type = TableWidthUnitValues.Dxa };
        }

        return newTotal;
    }

    private static void SetStyle(Table table, string style, string borderColor, MainDocumentPart mainPart, WordDesign design)
    {
        var properties = table.GetFirstChild<TableProperties>() ?? table.PrependChild(new TableProperties());
        var color = string.IsNullOrWhiteSpace(borderColor) ? design.TableBorderColor : ColorOf(borderColor);

        switch (style.Trim().ToLowerInvariant())
        {
            case "data":
            case "striped":
            case "banded":
            case "default":
                properties.TableStyle = new TableStyle { Val = WordStyleSheet.Ensure(mainPart, WordStyleSheet.DataTable, design) };
                properties.TableBorders = string.IsNullOrWhiteSpace(borderColor) ? null : Borders(color, verticals: true, outerSides: true);

                break;

            case "grid":
            case "bordered":
                properties.TableStyle = new TableStyle { Val = WordStyleSheet.Ensure(mainPart, WordStyleSheet.TableGrid, design) };
                properties.TableBorders = string.IsNullOrWhiteSpace(borderColor) ? null : Borders(color, verticals: true, outerSides: true);

                break;

            case "light":
            case "minimal":
            case "lines":
                properties.TableStyle = new TableStyle { Val = WordStyleSheet.Ensure(mainPart, WordStyleSheet.TableGrid, design) };
                properties.TableBorders = Borders(color, verticals: false, outerSides: false);

                break;

            case "plain":
            case "none":
            case "borderless":
                properties.TableStyle = new TableStyle { Val = WordStyleSheet.Ensure(mainPart, WordStyleSheet.TableGrid, design) };
                properties.TableBorders = new TableBorders(
                    new TopBorder { Val = BorderValues.Nil },
                    new LeftBorder { Val = BorderValues.Nil },
                    new BottomBorder { Val = BorderValues.Nil },
                    new RightBorder { Val = BorderValues.Nil },
                    new InsideHorizontalBorder { Val = BorderValues.Nil },
                    new InsideVerticalBorder { Val = BorderValues.Nil });

                break;

            default:
                properties.TableStyle = new TableStyle
                {
                    Val = WordStyleSheet.Find(mainPart, style, StyleValues.Table)
                        ?? throw new WordToolException($"There is no table style named \"{style}\". Use data, grid, light or plain, or a table style the document has (manage_word_styles lists them)."),
                };

                properties.TableBorders = string.IsNullOrWhiteSpace(borderColor) ? null : Borders(color, verticals: true, outerSides: true);

                break;
        }
    }

    private static TableBorders Borders(string color, bool verticals, bool outerSides)
    {
        var sides = outerSides ? BorderValues.Single : BorderValues.Nil;

        return new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 6U, Space = 0U, Color = color },
            new LeftBorder { Val = sides, Size = 6U, Space = 0U, Color = color },
            new BottomBorder { Val = BorderValues.Single, Size = 6U, Space = 0U, Color = color },
            new RightBorder { Val = sides, Size = 6U, Space = 0U, Color = color },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = color },
            new InsideVerticalBorder { Val = verticals ? BorderValues.Single : BorderValues.Nil, Size = 4U, Space = 0U, Color = color });
    }

    // The table look is kept as the original bit mask, which every version reads: 0x0200 turns the style's
    // row banding off.
    private static void SetBanded(Table table, bool banded)
    {
        var properties = table.GetFirstChild<TableProperties>() ?? table.PrependChild(new TableProperties());
        var look = properties.TableLook ??= new TableLook();
        var mask = int.TryParse(look.Val?.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : 0x0420;

        mask = banded ? mask & ~0x0200 : mask | 0x0200;
        look.Val = mask.ToString("X4", CultureInfo.InvariantCulture);

        if (look.NoHorizontalBand is not null)
        {
            look.NoHorizontalBand = !banded;
        }
    }

    private static string ColorOf(string value)
    {
        return WordColor.TryParse(value, out var hex)
            ? hex
            : throw new WordToolException($"\"{value}\" is not a color; use a hex value such as #1F4E79 or a color name.");
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

    // A table's columns are laid out from its grid. After columns change, the grid keeps the table's width,
    // each column keeping its share of it, and every cell of this table (not of a table nested in one) gets
    // the width of the columns it covers.
    private static void Fit(Table table, int tableWidth)
    {
        var grid = table.GetFirstChild<TableGrid>().Elements<GridColumn>().ToList();
        var widths = grid.Select(Width).ToArray();
        var known = widths.Where(width => width > 0).ToList();
        var fallback = known.Count > 0 ? (int)known.Average() : DefaultTableWidthTwips / Math.Max(1, grid.Count);

        for (var index = 0; index < widths.Length; index++)
        {
            if (widths[index] <= 0)
            {
                widths[index] = fallback;
            }
        }

        var sum = widths.Sum();
        var total = tableWidth > 0 ? tableWidth : sum;

        for (var index = 0; index < widths.Length; index++)
        {
            widths[index] = Math.Max(1, (int)Math.Round((double)widths[index] * total / sum));
            grid[index].Width = Invariant(widths[index]);
        }

        foreach (var row in table.Elements<TableRow>())
        {
            foreach (var slot in Slots(row))
            {
                var width = 0;

                for (var column = slot.Start; column <= slot.End && column <= widths.Length; column++)
                {
                    width += widths[column - 1];
                }

                (slot.Cell.TableCellProperties ??= new TableCellProperties()).TableCellWidth = new TableCellWidth { Width = Invariant(width), Type = TableWidthUnitValues.Dxa };
            }
        }
    }

    private static TableRow FindRow(List<TableRow> rows, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && !int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            var label = value.GetString().Trim();

            return rows.FirstOrDefault(row => row.Elements<TableCell>().FirstOrDefault() is { } cell && string.Equals(WordText.OfCell(cell).Trim(), label, StringComparison.OrdinalIgnoreCase))
                ?? throw new WordToolException($"No row starts with \"{label}\". The rows start with: {string.Join(", ", rows.Select(row => row.Elements<TableCell>().FirstOrDefault() is { } cell ? "\"" + WordText.OfCell(cell) + "\"" : "(empty)"))}.");
        }

        var number = (int)(WordJsonValues.ReadDouble(value) ?? 0);

        return number >= 1 && number <= rows.Count ? rows[number - 1] : throw new WordToolException($"There is no row {number}; the table has {rows.Count}.");
    }

    private static string Text(JsonElement value)
    {
        return WordJsonValues.ReadScalar(value) switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString(),
        };
    }

    private static string Invariant(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
