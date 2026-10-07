using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using CrestApps.Core.AI.Documents.Presentations.Rendering;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Builds and changes slide tables.
/// </summary>
internal sealed partial class OpenXmlPresentationEditor
{
    private const string NoTableStyle = "{2D5ABB26-0587-4C30-8999-92F81FD0307C}";
    private const long CellMarginX = 91_440;
    private const long CellMarginY = 45_720;

    private static readonly PresentationTableStyle _defaultTableStyle = new()
    {
        HeaderFill = "accent1",
        HeaderTextColor = "background1",
        HeaderBold = true,
        BodyFill = "background1",
        BandFill = "accent1 lighter 85%",
        TextColor = "text1",
        BorderColor = "accent1 lighter 60%",
        BorderWidth = 0.75,
        BandedRows = true,
    };

    private P.GraphicFrame BuildTable(SlidePart slidePart, PresentationElementSpec spec, PresentationBounds area, uint id)
    {
        var table = spec.Table;

        if (table?.Rows is not { Count: > 0 })
        {
            throw new PresentationEditException("A table needs rows: a list of rows, each a list of cell values, with the header first.");
        }

        var columnCount = table.Rows.Max(row => row?.Count ?? 0);

        if (columnCount == 0)
        {
            throw new PresentationEditException("The table's rows have no cells.");
        }

        if (table.Rows.Count > 60 || columnCount > 20)
        {
            throw new PresentationEditException($"A table of {table.Rows.Count.ToString(CultureInfo.InvariantCulture)} rows and {columnCount.ToString(CultureInfo.InvariantCulture)} columns will not be readable on a slide. Summarise it (for example with a query that aggregates) or split it across slides, keeping each under about 15 rows.");
        }

        var rows = table.Rows.Select(row => Enumerable.Range(0, columnCount).Select(index => row is not null && index < row.Count ? row[index] ?? string.Empty : string.Empty).ToList()).ToList();
        var style = PresentationTableStyle.Combine(_defaultTableStyle, PresentationTableStyle.Combine(HouseStyle.Table, table.Style));
        var fontSize = style.FontSize ?? AutomaticTableFontSize(rows.Count, columnCount);
        style.FontSize = fontSize;

        var bounds = ResolveBounds(spec.Bounds, area, area, naturalHeight: 1);
        var widths = ColumnWidths(rows, bounds.Width, table.ColumnWidths);
        var alignments = ColumnAlignments(rows, table.HeaderRow, table.ColumnAlignments);

        var tableElement = new A.Table(
            new A.TableProperties(new A.TableStyleId(NoTableStyle))
            {
                FirstRow = table.HeaderRow ? true : null,
                BandRow = style.BandedRows == true ? true : null,
            },
            new A.TableGrid(widths.Select(width => new A.GridColumn { Width = width })));

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = new A.TableRow { Height = PresentationUnits.EmusPerInch / 3 };

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                row.AppendChild(BuildCell(slidePart, rows[rowIndex][columnIndex], alignments[columnIndex]));
            }

            tableElement.AppendChild(row);
        }

        StyleTable(tableElement, style, table.HeaderRow);

        // Rows grow to fit their text, and a table that would run off the slide steps its text down in size
        // until it fits or reaches the smallest size still readable from the back of a room.
        var available = spec.Bounds?.Height?.ToEmus(SlideHeight) ?? (SlideHeight - bounds.Y - (SlideHeight / 20));
        var height = FitRowHeights(slidePart, tableElement);

        while (height > available && fontSize > 9)
        {
            fontSize -= 1;
            StyleTable(tableElement, new PresentationTableStyle { FontSize = fontSize }, table.HeaderRow);
            height = FitRowHeights(slidePart, tableElement);
        }

        if (height > available)
        {
            _result.Warnings.Add($"The {rows.Count.ToString(CultureInfo.InvariantCulture)}-row table is taller than the space left on the slide even at {fontSize.ToString(CultureInfo.InvariantCulture)} pt. Split it across slides or show fewer rows.");
        }

        var frameBounds = new PresentationBounds(bounds.X, bounds.Y, widths.Sum(), height);

        return new P.GraphicFrame(
            new P.NonVisualGraphicFrameProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = spec.Name ?? "Table " + id.ToString(CultureInfo.InvariantCulture) },
                new P.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.Transform(new A.Offset { X = frameBounds.X, Y = frameBounds.Y }, new A.Extents { Cx = frameBounds.Width, Cy = frameBounds.Height }),
            new A.Graphic(new A.GraphicData(tableElement) { Uri = OpenXmlPresentationConstants.TableGraphicUri }));
    }

    private static double AutomaticTableFontSize(int rows, int columns)
    {
        var size = rows switch
        {
            <= 5 => 16d,
            <= 8 => 14d,
            <= 12 => 12d,
            <= 18 => 11d,
            _ => 10d,
        };

        return columns switch
        {
            > 8 => Math.Min(size, 10),
            > 5 => Math.Min(size, 12),
            _ => size,
        };
    }

    private A.TableCell BuildCell(SlidePart slidePart, string text, string alignment)
    {
        var body = new A.TextBody(new A.BodyProperties(), new A.ListStyle());

        WriteParagraphs(slidePart, body, SplitLines(text ?? string.Empty), new PresentationTextStyle { Alignment = alignment }, explicitBullets: false);

        return new A.TableCell(
            body,
            new A.TableCellProperties
            {
                LeftMargin = (int)CellMarginX,
                RightMargin = (int)CellMarginX,
                TopMargin = (int)CellMarginY,
                BottomMargin = (int)CellMarginY,
                Anchor = A.TextAnchoringTypeValues.Center,
            });
    }

    /// <summary>
    /// Divides a table's width among its columns in proportion to how much each holds.
    /// </summary>
    private static long[] ColumnWidths(List<List<string>> rows, long totalWidth, IList<PresentationLength?> requested)
    {
        var count = rows[0].Count;
        var widths = new long[count];
        var weights = new double[count];

        for (var column = 0; column < count; column++)
        {
            var longest = rows.Max(row => (row[column] ?? string.Empty).Split('\n').Max(line => line.Length));
            weights[column] = Math.Clamp(longest, 4, 36);
        }

        var fixedTotal = 0L;
        var flexibleWeight = 0d;

        for (var column = 0; column < count; column++)
        {
            if (requested is not null && column < requested.Count && requested[column] is { } length)
            {
                widths[column] = length.ToEmus(totalWidth);
                fixedTotal += widths[column];
            }
            else
            {
                flexibleWeight += weights[column];
            }
        }

        var remaining = Math.Max(totalWidth - fixedTotal, count * PresentationUnits.EmusPerInch / 4);

        for (var column = 0; column < count; column++)
        {
            if (widths[column] == 0)
            {
                widths[column] = (long)(remaining * weights[column] / Math.Max(flexibleWeight, 1));
            }
        }

        return widths;
    }

    private static List<string> ColumnAlignments(List<List<string>> rows, bool headerRow, IList<string> requested)
    {
        var alignments = new List<string>();
        var body = headerRow ? rows.Skip(1).ToList() : rows;

        for (var column = 0; column < rows[0].Count; column++)
        {
            if (requested is not null && column < requested.Count && !string.IsNullOrWhiteSpace(requested[column]))
            {
                alignments.Add(requested[column].Trim().ToLowerInvariant());
                continue;
            }

            var values = body.Select(row => row[column]).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            alignments.Add(values.Count > 0 && values.All(IsNumeric) ? "right" : "left");
        }

        return alignments;
    }

    private static bool IsNumeric(string value)
    {
        var cleaned = value.Trim().Trim('$', '€', '£', '¥', '%', '+').Replace(",", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);

        if (cleaned.StartsWith('(') && cleaned.EndsWith(')'))
        {
            cleaned = cleaned[1..^1];
        }

        cleaned = cleaned.TrimStart('$', '€', '£', '¥').TrimEnd('%', 'k', 'K', 'm', 'M', 'b', 'B');

        return cleaned.Length > 0 && double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// Writes a table style onto every cell: fills, borders and text.
    /// </summary>
    private static void StyleTable(A.Table table, PresentationTableStyle style, bool? headerRow)
    {
        var rows = table.Elements<A.TableRow>().ToList();
        var properties = table.GetFirstChild<A.TableProperties>();
        var header = headerRow ?? properties?.FirstRow?.Value == true;
        var banded = style.BandedRows ?? properties?.BandRow?.Value == true;

        if (properties is not null)
        {
            if (headerRow is not null)
            {
                properties.FirstRow = headerRow.Value ? true : null;
            }

            if (style.BandedRows is { } band)
            {
                properties.BandRow = band ? true : null;
            }
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var isHeader = header && rowIndex == 0;
            var isTotal = style.TotalRow == true && rowIndex == rows.Count - 1 && rows.Count > 1;
            var bodyIndex = header ? rowIndex - 1 : rowIndex;
            var cells = rows[rowIndex].Elements<A.TableCell>().ToList();

            for (var columnIndex = 0; columnIndex < cells.Count; columnIndex++)
            {
                var cell = cells[columnIndex];
                var cellProperties = cell.TableCellProperties ?? cell.AppendChild(new A.TableCellProperties());
                string fill;

                if (isHeader)
                {
                    fill = style.HeaderFill;
                }
                else if (banded && bodyIndex % 2 == 1)
                {
                    fill = style.BandFill ?? style.BodyFill;
                }
                else
                {
                    fill = style.BodyFill;
                }

                if (fill is not null)
                {
                    OpenXmlSchemaOrder.Set(cellProperties, OpenXmlDrawingWriter.Fill(OpenXmlDrawingWriter.ParseColor(fill, "table cell")), OpenXmlSchemaOrder.TableCellProperties, OpenXmlSchemaOrder.Fills);
                }

                if (style.BorderColor is not null || style.BorderWidth is not null)
                {
                    SetCellBorders(cellProperties, style.BorderColor ?? "text1", style.BorderWidth ?? 0.75, isTotal);
                }
                else if (isTotal)
                {
                    SetCellBorders(cellProperties, style.TextColor ?? "text1", 1.5, true);
                }

                var textStyle = new PresentationTextStyle
                {
                    Font = style.Font,
                    Size = style.FontSize,
                    Color = isHeader ? style.HeaderTextColor ?? style.TextColor : style.TextColor,
                    Bold = isHeader ? style.HeaderBold : isTotal ? true : columnIndex == 0 && style.FirstColumnBold == true ? true : null,
                };

                if (cell.TextBody is { } body && !textStyle.IsEmpty)
                {
                    ApplyTextStyle(body, null, textStyle);
                }
            }
        }
    }

    private static void SetCellBorders(A.TableCellProperties properties, string color, double width, bool emphasiseTop)
    {
        var parsed = OpenXmlDrawingWriter.ParseColor(color, "table border");
        var emus = (int)PresentationUnits.FromPoints(parsed.IsNone ? 0 : width);

        OpenXmlElement Line<T>(int lineWidth)
            where T : A.LinePropertiesType, new()
        {
            var line = new T { Width = lineWidth };
            line.AppendChild(parsed.IsNone ? new A.NoFill() : OpenXmlDrawingWriter.Fill(parsed));

            return line;
        }

        OpenXmlSchemaOrder.Set(properties, Line<A.LeftBorderLineProperties>(emus), OpenXmlSchemaOrder.TableCellProperties);
        OpenXmlSchemaOrder.Set(properties, Line<A.RightBorderLineProperties>(emus), OpenXmlSchemaOrder.TableCellProperties);
        OpenXmlSchemaOrder.Set(properties, Line<A.TopBorderLineProperties>(emphasiseTop ? emus * 2 : emus), OpenXmlSchemaOrder.TableCellProperties);
        OpenXmlSchemaOrder.Set(properties, Line<A.BottomBorderLineProperties>(emus), OpenXmlSchemaOrder.TableCellProperties);
    }

    /// <summary>
    /// Sets each row's height to what its text needs, the way PowerPoint grows rows, and returns the table's
    /// total height.
    /// </summary>
    private long FitRowHeights(SlidePart slidePart, A.Table table)
    {
        var context = OpenXmlSlideContext.ForSlide(slidePart, _themes);
        var widths = table.TableGrid.Elements<A.GridColumn>().Select(column => column.Width?.Value ?? 0).ToList();
        var total = 0L;

        foreach (var row in table.Elements<A.TableRow>())
        {
            var needed = 0d;
            var columnIndex = 0;

            foreach (var cell in row.Elements<A.TableCell>())
            {
                var span = Math.Max(1, cell.GridSpan?.Value ?? 1);
                var width = widths.Skip(columnIndex).Take(span).Sum();
                columnIndex += span;

                if (cell.HorizontalMerge?.Value == true || cell.VerticalMerge?.Value == true)
                {
                    continue;
                }

                var body = OpenXmlTextReader.Read(
                    cell.TextBody,
                    context,
                    [OpenXmlMarkup.Child(cell.TextBody, "lstStyle"), context.DefaultTextStyle],
                    [],
                    new OpenXmlTextDefaults { Font = context.Theme.MinorFont, Size = 18 },
                    null);

                body.InsetLeft = cell.TableCellProperties?.LeftMargin?.Value ?? CellMarginX;
                body.InsetRight = cell.TableCellProperties?.RightMargin?.Value ?? CellMarginX;
                body.InsetTop = cell.TableCellProperties?.TopMargin?.Value ?? CellMarginY;
                body.InsetBottom = cell.TableCellProperties?.BottomMargin?.Value ?? CellMarginY;

                var layout = PresentationTextLayout.Layout(body, 0, 0, PresentationUnits.ToPoints(width), 10_000);
                needed = Math.Max(needed, layout.RequiredHeight);
            }

            var height = Math.Max(PresentationUnits.FromPoints(needed), PresentationUnits.EmusPerPoint * 14);
            row.Height = height;
            total += height;
        }

        return total;
    }

    private void UpdateTable(UpdateTableEdit edit)
    {
        var slidePart = GetSlide(edit.Slide);
        var frame = FindGraphic(slidePart, edit.Element, edit.Slide, OpenXmlPresentationConstants.TableGraphicUri, "table");
        var table = frame.Descendants<A.Table>().First();
        var changes = new List<string>();

        if (edit.ReplaceRows is { Count: > 0 })
        {
            ReplaceRows(slidePart, table, edit.ReplaceRows);
            changes.Add($"replaced its content with {edit.ReplaceRows.Count.ToString(CultureInfo.InvariantCulture)} row(s)");
        }

        foreach (var number in (edit.DeleteRows ?? []).Distinct().OrderByDescending(number => number))
        {
            var rows = table.Elements<A.TableRow>().ToList();

            if (number < 1 || number > rows.Count)
            {
                throw new PresentationEditException($"The table has {rows.Count.ToString(CultureInfo.InvariantCulture)} row(s), so there is no row {number.ToString(CultureInfo.InvariantCulture)}.");
            }

            rows[number - 1].Remove();
            changes.Add($"deleted row {number.ToString(CultureInfo.InvariantCulture)}");
        }

        foreach (var number in (edit.DeleteColumns ?? []).Distinct().OrderByDescending(number => number))
        {
            var columns = table.TableGrid.Elements<A.GridColumn>().ToList();

            if (number < 1 || number > columns.Count)
            {
                throw new PresentationEditException($"The table has {columns.Count.ToString(CultureInfo.InvariantCulture)} column(s), so there is no column {number.ToString(CultureInfo.InvariantCulture)}.");
            }

            columns[number - 1].Remove();

            foreach (var row in table.Elements<A.TableRow>())
            {
                row.Elements<A.TableCell>().ElementAtOrDefault(number - 1)?.Remove();
            }

            changes.Add($"deleted column {number.ToString(CultureInfo.InvariantCulture)}");
        }

        if (edit.InsertRows is { Count: > 0 })
        {
            var rows = table.Elements<A.TableRow>().ToList();
            var after = Math.Clamp(edit.InsertRowsAfter ?? rows.Count, 0, rows.Count);
            var template = rows.Count == 0 ? null : rows[Math.Clamp(after == 0 ? Math.Min(1, rows.Count - 1) : after - 1, 0, rows.Count - 1)];
            OpenXmlElement anchor = after == 0 ? null : rows[after - 1];

            foreach (var values in edit.InsertRows)
            {
                var row = CloneRow(slidePart, template, table, values);

                if (anchor is null)
                {
                    table.InsertAfter(row, table.TableGrid);
                }
                else
                {
                    table.InsertAfter(row, anchor);
                }

                anchor = row;
            }

            changes.Add($"inserted {edit.InsertRows.Count.ToString(CultureInfo.InvariantCulture)} row(s)");
        }

        if (edit.InsertColumns is { Count: > 0 })
        {
            InsertColumns(slidePart, table, edit.InsertColumns, edit.InsertColumnsAfter);
            changes.Add($"inserted {edit.InsertColumns.Count.ToString(CultureInfo.InvariantCulture)} column(s)");
        }

        if (edit.ColumnWidths is { Count: > 0 })
        {
            var columns = table.TableGrid.Elements<A.GridColumn>().ToList();
            var total = columns.Sum(column => column.Width?.Value ?? 0);

            for (var index = 0; index < columns.Count && index < edit.ColumnWidths.Count; index++)
            {
                if (edit.ColumnWidths[index] is { } requestedWidth)
                {
                    columns[index].Width = requestedWidth.ToEmus(total);
                }
            }

            changes.Add("set its column widths");
        }

        if (edit.Style is not null || edit.HeaderRow is not null)
        {
            StyleTable(table, edit.Style ?? new PresentationTableStyle(), edit.HeaderRow);
            changes.Add("restyled it");
        }

        foreach (var cellEdit in edit.Cells ?? [])
        {
            UpdateCell(slidePart, table, cellEdit);
        }

        if (edit.Cells is { Count: > 0 })
        {
            changes.Add($"changed {edit.Cells.Count.ToString(CultureInfo.InvariantCulture)} cell(s)");
        }

        if (changes.Count == 0)
        {
            throw new PresentationEditException("Nothing to change was given for the table.");
        }

        // The frame follows the table, so the picture and the file agree on where it ends.
        var height = FitRowHeights(slidePart, table);
        var width = table.TableGrid.Elements<A.GridColumn>().Sum(column => column.Width?.Value ?? 0);
        var current = Resolve(slidePart, frame).Bounds;
        SetBounds(frame, current with { Width = width, Height = height });

        MarkChanged(slidePart);
        _result.Changes.Add($"On slide {edit.Slide.ToString(CultureInfo.InvariantCulture)}, table {ElementId(frame).ToString(CultureInfo.InvariantCulture)}: {string.Join(", ", changes)}.");
    }

    private static OpenXmlElement FindGraphic(SlidePart slidePart, string reference, int slideNumber, string uri, string kind)
    {
        if (!string.IsNullOrWhiteSpace(reference))
        {
            var element = FindElement(slidePart, reference, slideNumber);

            if (element.LocalName != "graphicFrame" || OpenXmlMarkup.Attribute(OpenXmlMarkup.Path(element, "graphic", "graphicData"), "uri") != uri)
            {
                throw new PresentationEditException($"{DescribeElement(element)} is not a {kind}.");
            }

            return element;
        }

        var candidates = Elements(ShapeTree(slidePart))
            .Where(element => element.LocalName == "graphicFrame" && OpenXmlMarkup.Attribute(OpenXmlMarkup.Path(element, "graphic", "graphicData"), "uri") == uri)
            .ToList();

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new PresentationEditException($"Slide {slideNumber.ToString(CultureInfo.InvariantCulture)} has no {kind}."),
            _ => throw new PresentationEditException($"Slide {slideNumber.ToString(CultureInfo.InvariantCulture)} has {candidates.Count.ToString(CultureInfo.InvariantCulture)} {kind}s; name one: {string.Join("; ", candidates.Select(DescribeElement))}."),
        };
    }

    private void ReplaceRows(SlidePart slidePart, A.Table table, IList<IList<string>> values)
    {
        var rows = table.Elements<A.TableRow>().ToList();
        var header = table.GetFirstChild<A.TableProperties>()?.FirstRow?.Value == true;
        var headerTemplate = rows.Count > 0 ? rows[0] : null;
        var bodyTemplates = rows.Skip(header ? 1 : 0).Take(2).ToList();
        var columnCount = values.Max(row => row?.Count ?? 0);

        ResizeGrid(table, columnCount);

        foreach (var row in rows)
        {
            row.Remove();
        }

        for (var index = 0; index < values.Count; index++)
        {
            var template = header && index == 0
                ? headerTemplate
                : bodyTemplates.Count == 0 ? headerTemplate : bodyTemplates[(header ? index - 1 : index) % bodyTemplates.Count];

            table.AppendChild(CloneRow(slidePart, template, table, values[index]));
        }
    }

    private static void ResizeGrid(A.Table table, int columnCount)
    {
        var grid = table.TableGrid;
        var columns = grid.Elements<A.GridColumn>().ToList();
        var total = columns.Sum(column => column.Width?.Value ?? 0);

        if (columnCount == columns.Count || columnCount <= 0)
        {
            return;
        }

        foreach (var column in columns)
        {
            column.Remove();
        }

        var width = total / columnCount;

        for (var index = 0; index < columnCount; index++)
        {
            grid.AppendChild(new A.GridColumn { Width = width });
        }
    }

    private A.TableRow CloneRow(SlidePart slidePart, A.TableRow template, A.Table table, IList<string> values)
    {
        var columnCount = table.TableGrid.Elements<A.GridColumn>().Count();
        var row = new A.TableRow { Height = template?.Height?.Value ?? PresentationUnits.EmusPerInch / 3 };
        var templateCells = template?.Elements<A.TableCell>().ToList() ?? [];

        for (var index = 0; index < columnCount; index++)
        {
            A.TableCell cell;

            if (templateCells.Count > 0)
            {
                cell = (A.TableCell)templateCells[Math.Min(index, templateCells.Count - 1)].CloneNode(true);
                cell.GridSpan = null;
                cell.RowSpan = null;
                cell.HorizontalMerge = null;
                cell.VerticalMerge = null;
            }
            else
            {
                cell = BuildCell(slidePart, string.Empty, "left");
            }

            var text = values is not null && index < values.Count ? values[index] ?? string.Empty : string.Empty;
            WriteParagraphs(slidePart, cell.TextBody, SplitLines(text), null, explicitBullets: false);
            row.AppendChild(cell);
        }

        return row;
    }

    private void InsertColumns(SlidePart slidePart, A.Table table, IList<IList<string>> columns, int? after)
    {
        var grid = table.TableGrid;
        var gridColumns = grid.Elements<A.GridColumn>().ToList();
        var total = gridColumns.Sum(column => column.Width?.Value ?? 0);
        var position = Math.Clamp(after ?? gridColumns.Count, 0, gridColumns.Count);
        var templateIndex = Math.Clamp(position == 0 ? 0 : position - 1, 0, Math.Max(0, gridColumns.Count - 1));
        var templateWidth = gridColumns.Count == 0 ? total : gridColumns[templateIndex].Width?.Value ?? 0;

        for (var offset = columns.Count - 1; offset >= 0; offset--)
        {
            var newColumn = new A.GridColumn { Width = templateWidth };

            if (position == 0 || gridColumns.Count == 0)
            {
                grid.PrependChild(newColumn);
            }
            else
            {
                grid.InsertAfter(newColumn, gridColumns[position - 1]);
            }

            var rowIndex = 0;

            foreach (var row in table.Elements<A.TableRow>())
            {
                var cells = row.Elements<A.TableCell>().ToList();
                var template = cells.Count == 0 ? BuildCell(slidePart, string.Empty, "left") : (A.TableCell)cells[Math.Min(templateIndex, cells.Count - 1)].CloneNode(true);
                template.GridSpan = null;
                template.HorizontalMerge = null;

                var values = columns[offset];
                var text = values is not null && rowIndex < values.Count ? values[rowIndex] ?? string.Empty : string.Empty;
                WriteParagraphs(slidePart, template.TextBody, SplitLines(text), null, explicitBullets: false);

                if (position == 0 || cells.Count == 0)
                {
                    row.PrependChild(template);
                }
                else
                {
                    row.InsertAfter(template, cells[Math.Min(position - 1, cells.Count - 1)]);
                }

                rowIndex++;
            }
        }

        // The table keeps its width: every column gives up a share for the new ones.
        var columnsAfter = grid.Elements<A.GridColumn>().ToList();
        var newTotal = columnsAfter.Sum(column => column.Width?.Value ?? 0);

        if (newTotal > 0 && total > 0)
        {
            foreach (var column in columnsAfter)
            {
                column.Width = (long)((column.Width?.Value ?? 0) * (total / (double)newTotal));
            }
        }
    }

    private void UpdateCell(SlidePart slidePart, A.Table table, PresentationTableCellEdit edit)
    {
        var rows = table.Elements<A.TableRow>().ToList();

        if (edit.Row < 1 || edit.Row > rows.Count)
        {
            throw new PresentationEditException($"The table has {rows.Count.ToString(CultureInfo.InvariantCulture)} row(s), so there is no row {edit.Row.ToString(CultureInfo.InvariantCulture)}.");
        }

        var cells = rows[edit.Row - 1].Elements<A.TableCell>().ToList();

        if (edit.Column < 1 || edit.Column > cells.Count)
        {
            throw new PresentationEditException($"The table has {cells.Count.ToString(CultureInfo.InvariantCulture)} column(s), so there is no column {edit.Column.ToString(CultureInfo.InvariantCulture)}.");
        }

        var cell = cells[edit.Column - 1];

        if (edit.Text is not null)
        {
            WriteParagraphs(slidePart, cell.TextBody, SplitLines(edit.Text), edit.TextStyle, explicitBullets: false);
        }
        else if (edit.TextStyle is not null)
        {
            ApplyTextStyle(cell.TextBody, null, edit.TextStyle);
        }

        if (edit.Fill is not null)
        {
            var properties = cell.TableCellProperties ?? cell.AppendChild(new A.TableCellProperties());
            OpenXmlSchemaOrder.Set(properties, OpenXmlDrawingWriter.Fill(OpenXmlDrawingWriter.ParseColor(edit.Fill, "table cell")), OpenXmlSchemaOrder.TableCellProperties, OpenXmlSchemaOrder.Fills);
        }

        if (edit.ColumnSpan is > 1 and var columnSpan)
        {
            cell.GridSpan = Math.Min(columnSpan, cells.Count - edit.Column + 1);

            for (var index = edit.Column; index < edit.Column - 1 + cell.GridSpan.Value; index++)
            {
                cells[index].HorizontalMerge = true;
            }
        }

        if (edit.RowSpan is > 1 and var rowSpan)
        {
            cell.RowSpan = Math.Min(rowSpan, rows.Count - edit.Row + 1);

            for (var index = edit.Row; index < edit.Row - 1 + cell.RowSpan.Value; index++)
            {
                var below = rows[index].Elements<A.TableCell>().ElementAtOrDefault(edit.Column - 1);

                if (below is not null)
                {
                    below.VerticalMerge = true;
                }
            }
        }
    }
}
