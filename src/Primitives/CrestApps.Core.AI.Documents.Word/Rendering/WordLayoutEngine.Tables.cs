using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <content>
/// Tables: their grid, rows and cells laid out, and rows placed on pages with the header row repeated.
/// </content>
internal sealed partial class WordLayoutEngine
{
    // Word's tables have at most 63 columns; a grid or a cell span asking for more is held to it.
    private const int MaxTableColumns = 63;

    private const int RowBatch = 64;

    private void PlaceTable(Table table)
    {
        if (_stopped || _page is null)
        {
            return;
        }

        var context = new LayoutContext(_package.MainPart, _resolver.ResolveTable(table));
        var allRows = table.Elements<TableRow>().ToList();
        var headerCount = HeaderCount(allRows);
        List<RowLayout> headers = null;
        var start = 0;

        // Rows are laid out a batch at a time, so a table of a million rows stops being laid out once the page
        // limit is reached rather than all being measured first.
        while (start < allRows.Count && !_stopped)
        {
            var end = BatchEnd(allRows, start, Math.Max(headerCount, start + RowBatch));
            var rows = LayoutRows(table, allRows, start, end - start, ColumnWidth, context, out var offset, out var tableWidth);

            if (start == 0 && tableWidth > ColumnWidth + 1)
            {
                Issue("overflow", $"Table {DescribeBlock(table)} is wider than the text column ({tableWidth:0} pt for {ColumnWidth:0} pt).", table);
            }

            headers ??= rows.TakeWhile(row => row.IsHeader).ToList();

            foreach (var row in rows)
            {
                if (_y + row.Height > Bottom && !AtTopOfColumn)
                {
                    NextColumnOrPage(flowed: true);

                    if (_stopped)
                    {
                        return;
                    }

                    // The header rows repeat at the top of every page the table runs onto. They are laid out again
                    // for each page, as their items are placed once, without numbering their lists again.
                    if (!row.IsHeader && headers.Count > 0 && headers.Sum(header => header.Height) + row.Height < Bottom - _y)
                    {
                        foreach (var header in headers)
                        {
                            PlaceRow(Measure(() => LayoutRows(table, allRows, header.Index, 1, ColumnWidth, context, out _, out _))[0], offset, table);
                        }
                    }
                }

                if (row.Height > Bottom - _columnTop)
                {
                    Issue("clipped", $"A row of table {DescribeBlock(table)} is taller than the page and is cut off.", table);
                }

                PlaceRow(row, offset, table);
            }

            start = end;
        }

        _lastSpaceAfter = 0;
        _lastStyle = null;
    }

    private static int HeaderCount(List<TableRow> rows)
    {
        return rows.TakeWhile(row => row.TableRowProperties?.GetFirstChild<TableHeader>() is not null).Count();
    }

    // A batch ends at the first row from the target on that does not continue a vertically merged cell, so a
    // merged cell is always measured with all the rows it spans.
    private static int BatchEnd(List<TableRow> rows, int start, int target)
    {
        var end = Math.Clamp(target, start + 1, rows.Count);

        while (end < rows.Count && rows[end].Elements<TableCell>().Any(cell => cell.TableCellProperties?.VerticalMerge is { } merge && (merge.Val is null || merge.Val.InnerText == "continue")))
        {
            end++;
        }

        return end;
    }

    private void PlaceRow(RowLayout row, double offset, Table table)
    {
        if (_page is null)
        {
            return;
        }

        foreach (var item in row.Box.Translate(ColumnLeft + offset, _y))
        {
            AddToPage(item);
        }

        _y += row.Height;
        _page.BodyBottom = Math.Max(_page.BodyBottom, _y);
        Record(table);
        Record(row.Element);
    }

    private WordBox LayoutTableBox(Table table, double width, LayoutContext outer)
    {
        var context = new LayoutContext(outer.Part, _resolver.ResolveTable(table));
        var allRows = table.Elements<TableRow>().ToList();
        var rows = LayoutRows(table, allRows, 0, allRows.Count, width, context, out var offset, out _);
        var box = new WordBox();
        var y = 0d;

        foreach (var row in rows)
        {
            box.Items.AddRange(row.Box.Translate(offset, y));
            y += row.Height;
        }

        box.Height = y;

        return box;
    }

    /// <summary>
    /// Lays out some of a table's rows.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="allRows">Every row of the table.</param>
    /// <param name="first">The index of the first row to lay out.</param>
    /// <param name="count">The number of rows to lay out.</param>
    /// <param name="available">The width the table is laid out in.</param>
    /// <param name="context">The table's layout context.</param>
    /// <param name="offset">The table's distance from the left edge.</param>
    /// <param name="tableWidth">The table's width.</param>
    /// <returns>The rows.</returns>
    private List<RowLayout> LayoutRows(Table table, List<TableRow> allRows, int first, int count, double available, LayoutContext context, out double offset, out double tableWidth)
    {
        var properties = table.GetFirstChild<TableProperties>();
        var style = context.Table;
        var columns = GridWidths(table, properties, available);

        // Each cell's left edge is the sum of the columns before it; the sums are taken once, not per cell.
        var edges = new double[columns.Length + 1];

        for (var index = 0; index < columns.Length; index++)
        {
            edges[index + 1] = edges[index] + columns[index];
        }

        tableWidth = edges[^1];

        var indent = Math.Clamp((properties?.TableIndentation?.Width?.Value ?? 0) / 20d, -MaxPageSide, MaxPageSide);

        offset = (properties?.TableJustification?.Val?.InnerText) switch
        {
            "center" => Math.Max(0, (available - tableWidth) / 2),
            "right" or "end" => Math.Max(0, available - tableWidth),
            _ => indent,
        };

        var look = ReadLook(properties?.TableLook);
        var defaultMargins = properties?.TableCellMarginDefault ?? style.CellMargins;
        var borders = properties?.TableBorders;
        var headerCount = HeaderCount(allRows);
        var last = Math.Min(allRows.Count, first + count);
        var layouts = new List<RowLayout>(Math.Max(0, last - first));
        var mergeOrigins = new Dictionary<int, (RowLayout Row, int Cell)>();

        for (var rowIndex = Math.Max(0, first); rowIndex < last; rowIndex++)
        {
            var rowElement = allRows[rowIndex];
            var isFirst = rowIndex == 0;
            var isLast = rowIndex == allRows.Count - 1;
            var isHeader = rowIndex < headerCount;

            // Banding counts every row but a styled first row.
            var bodyIndex = look.FirstRow ? rowIndex - 1 : rowIndex;
            var band = isFirst && look.FirstRow ? null : (bodyIndex % 2 == 0 ? "band1Horz" : "band2Horz");

            var row = new RowLayout { Element = rowElement, Index = rowIndex, Position = layouts.Count, IsHeader = isHeader };
            var cells = new List<CellLayout>();
            var column = 0;

            foreach (var cell in rowElement.Elements<TableCell>())
            {
                var cellProperties = cell.TableCellProperties;
                var span = Math.Clamp(cellProperties?.GridSpan?.Val?.Value ?? 1, 1, MaxTableColumns);
                var x = edges[Math.Min(column, columns.Length)];
                var width = edges[Math.Min(column + span, columns.Length)] - x;
                var merge = cellProperties?.VerticalMerge;
                var continues = merge is not null && (merge.Val is null || merge.Val.InnerText == "continue");
                var conditions = new List<WordTableStyleCondition>();

                void Condition(bool applies, string type)
                {
                    if (applies && style.Conditional.TryGetValue(type, out var condition))
                    {
                        conditions.Add(condition);
                    }
                }

                Condition(!look.NoHorizontalBand && band is not null, band ?? string.Empty);
                Condition(!look.NoVerticalBand && span == 1, column % 2 == 0 ? "band1Vert" : "band2Vert");
                Condition(look.FirstColumn && column == 0, "firstCol");
                Condition(look.LastColumn && column + span >= columns.Length, "lastCol");
                Condition(look.FirstRow && isFirst, "firstRow");
                Condition(look.LastRow && isLast, "lastRow");

                var fill = _resolver.ResolveColor(cellProperties?.Shading?.Fill?.Value, cellProperties?.Shading?.ThemeFill?.InnerText)
                    ?? conditions.Select(condition => condition.Fill).LastOrDefault(value => !string.IsNullOrEmpty(value) && !string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase));

                var margins = CellMargins(cellProperties?.TableCellMargin, defaultMargins);
                var contentWidth = Math.Max(4, width - margins.Left - margins.Right);
                // A cell past the table's last column has no room to be drawn in.
                var content = continues || column >= columns.Length
                    ? new WordBox()
                    : LayoutContainer(cell.ChildElements.Where(child => child is not TableCellProperties), contentWidth, context with { Conditions = conditions });

                var layout = new CellLayout
                {
                    Element = cell,
                    X = x,
                    Width = width,
                    Content = content,
                    Margins = margins,
                    Fill = fill is null || string.Equals(fill, "auto", StringComparison.OrdinalIgnoreCase) ? null : fill,
                    Continues = continues,
                    Restarts = merge?.Val?.InnerText == "restart",
                    Column = column,
                    Span = span,
                    Borders = cellProperties?.TableCellBorders,
                    VerticalAlignment = cellProperties?.TableCellVerticalAlignment?.Val?.InnerText ?? "top",
                    LastColumn = column + span >= columns.Length,
                };

                cells.Add(layout);
                column += span;
            }

            // A merged cell's content is measured against all the rows it spans, after they are known.
            var contentHeight = cells.Where(cell => !cell.Continues && !cell.Restarts).Select(cell => cell.Content.Height + cell.Margins.Top + cell.Margins.Bottom).DefaultIfEmpty(0).Max();
            var height = contentHeight > 0 ? contentHeight : WordTextMeasurer.LineHeight("Calibri", 11) + 2;
            var rule = rowElement.TableRowProperties?.GetFirstChild<TableRowHeight>();

            if (rule?.Val?.Value is { } requested)
            {
                var points = requested / 20d;

                height = rule.HeightType?.InnerText == "exact" ? points : Math.Max(height, points);
            }

            row.Height = height;
            row.Cells = cells;
            row.IsFirst = isFirst;
            row.IsLast = isLast;
            layouts.Add(row);

            // A merged cell's content counts against the rows it spans; the last of them grows if it does not fit.
            for (var cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                var cell = cells[cellIndex];

                if (cell.Restarts)
                {
                    mergeOrigins[cell.Column] = (row, cellIndex);
                }
                else if (!cell.Continues)
                {
                    CloseMerge(mergeOrigins, layouts, cell.Column);
                }
            }
        }

        foreach (var column in mergeOrigins.Keys.ToList())
        {
            CloseMerge(mergeOrigins, layouts, column);
        }

        foreach (var row in layouts)
        {
            row.Box = DrawRow(row, layouts, borders, style.Borders);
        }

        return layouts;
    }

    private static void CloseMerge(Dictionary<int, (RowLayout Row, int Cell)> origins, List<RowLayout> rows, int column)
    {
        if (!origins.Remove(column, out var origin))
        {
            return;
        }

        var cell = origin.Row.Cells[origin.Cell];
        var start = origin.Row.Position;
        var end = start;

        while (end + 1 < rows.Count && rows[end + 1].Cells.Any(other => other.Column == column && other.Continues))
        {
            end++;
        }

        var needed = cell.Content.Height + cell.Margins.Top + cell.Margins.Bottom;
        var spanned = rows.Skip(start).Take(end - start + 1).Sum(row => row.Height);

        if (needed > spanned)
        {
            rows[end].Height += needed - spanned;
        }

        cell.MergedRows = end - start + 1;
    }

    private WordBox DrawRow(RowLayout row, List<RowLayout> rows, TableBorders direct, TableBorders styled)
    {
        var box = new WordBox { Height = row.Height };
        var start = row.Position;
        var next = start + 1 < rows.Count ? rows[start + 1] : null;

        foreach (var cell in row.Cells)
        {
            var height = cell.Restarts ? rows.Skip(start).Take(Math.Max(1, cell.MergedRows)).Sum(other => other.Height) : row.Height;

            if (cell.Fill is not null && !cell.Continues)
            {
                box.Items.Add(new WordRectItem { X = cell.X, Y = 0, Width = cell.Width, Height = height, Fill = cell.Fill, Source = cell.Element });
            }

            if (!cell.Continues)
            {
                var top = cell.VerticalAlignment switch
                {
                    "center" => Math.Max(cell.Margins.Top, (height - cell.Content.Height) / 2),
                    "bottom" => Math.Max(cell.Margins.Top, height - cell.Content.Height - cell.Margins.Bottom),
                    _ => cell.Margins.Top,
                };

                box.Items.AddRange(cell.Content.Translate(cell.X + cell.Margins.Left, top));
            }

            var topBorder = EdgeBorder(cell.Borders?.TopBorder, row.IsFirst ? direct?.TopBorder : direct?.InsideHorizontalBorder, row.IsFirst ? styled?.TopBorder : styled?.InsideHorizontalBorder);
            var bottomBorder = EdgeBorder(cell.Borders?.BottomBorder, row.IsLast ? direct?.BottomBorder : direct?.InsideHorizontalBorder, row.IsLast ? styled?.BottomBorder : styled?.InsideHorizontalBorder);
            var leftBorder = EdgeBorder(cell.Borders?.LeftBorder ?? (BorderType)cell.Borders?.StartBorder, cell.Column == 0 ? (BorderType)direct?.LeftBorder ?? direct?.StartBorder : direct?.InsideVerticalBorder, cell.Column == 0 ? (BorderType)styled?.LeftBorder ?? styled?.StartBorder : styled?.InsideVerticalBorder);
            var rightBorder = EdgeBorder(cell.Borders?.RightBorder ?? (BorderType)cell.Borders?.EndBorder, cell.LastColumn ? (BorderType)direct?.RightBorder ?? direct?.EndBorder : direct?.InsideVerticalBorder, cell.LastColumn ? (BorderType)styled?.RightBorder ?? styled?.EndBorder : styled?.InsideVerticalBorder);

            if (!cell.Continues)
            {
                Edge(box, topBorder, cell.X, 0, cell.X + cell.Width, 0, cell.Element);
            }

            if (row.IsLast || next?.Cells.Any(other => other.Column == cell.Column && other.Continues) != true)
            {
                Edge(box, bottomBorder, cell.X, row.Height, cell.X + cell.Width, row.Height, cell.Element);
            }

            Edge(box, leftBorder, cell.X, 0, cell.X, row.Height, cell.Element);

            if (cell.LastColumn)
            {
                Edge(box, rightBorder, cell.X + cell.Width, 0, cell.X + cell.Width, row.Height, cell.Element);
            }
        }

        return box;
    }

    private WordBorder EdgeBorder(BorderType cell, BorderType table, BorderType styled)
    {
        return _resolver.Border(cell) ?? _resolver.Border(table) ?? _resolver.Border(styled);
    }

    private static void Edge(WordBox box, WordBorder border, double x1, double y1, double x2, double y2, OpenXmlElement source)
    {
        if (border is null || border.Style == "none" || border.Width <= 0)
        {
            return;
        }

        box.Items.Add(new WordLineItem { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Color = border.Color ?? "000000", Width = Math.Max(0.25, border.Width), Dotted = border.Style is "dotted" or "dashed", Source = source });
    }

    private static double[] GridWidths(Table table, TableProperties properties, double available)
    {
        // The column count and widths come from the document, so they are held to what Word allows: a cell
        // spanning a billion columns must not allocate a billion widths.
        var grid = table.GetFirstChild<TableGrid>()?.Elements<GridColumn>().Take(MaxTableColumns).Select(column => Math.Clamp(Twips(column.Width?.Value), 0, MaxPageSide)).ToArray() ?? [];
        var firstRow = table.Elements<TableRow>().FirstOrDefault();
        var spans = firstRow?.Elements<TableCell>().Take(MaxTableColumns).Sum(cell => (long)Math.Clamp(cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1, 1, MaxTableColumns)) ?? 1;
        var count = (int)Math.Clamp(Math.Max(grid.Length, spans), 1, MaxTableColumns);

        if (grid.Length < count || grid.All(width => width <= 0))
        {
            grid = [.. Enumerable.Repeat(available / count, count)];
        }

        var target = grid.Sum();
        var width = properties?.TableWidth;

        if (width?.Type?.InnerText == "pct" && double.TryParse(width.Width?.Value?.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) && double.IsFinite(percent))
        {
            target = available * Math.Clamp(width.Width.Value.EndsWith('%') ? percent / 100 : percent / 5000, 0, 10);
        }
        else if (width?.Type?.InnerText == "dxa" && double.TryParse(width.Width?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips) && double.IsFinite(twips) && twips > 0)
        {
            target = Math.Min(twips / 20, MaxPageSide * 10);
        }

        if (target <= 0)
        {
            target = available;
        }

        var sum = grid.Sum();

        return sum <= 0 ? grid : [.. grid.Select(column => column * target / sum)];
    }

    private static (double Top, double Right, double Bottom, double Left) CellMargins(TableCellMargin cell, TableCellMarginDefault table)
    {
        var left = Twips(cell?.LeftMargin?.Width?.Value) is > 0 and var cellLeft ? cellLeft : Twips(cell?.StartMargin?.Width?.Value) is > 0 and var cellStart ? cellStart : table?.TableCellLeftMargin?.Width?.Value is { } tableLeft ? tableLeft / 20d : 5.4;
        var right = Twips(cell?.RightMargin?.Width?.Value) is > 0 and var cellRight ? cellRight : Twips(cell?.EndMargin?.Width?.Value) is > 0 and var cellEnd ? cellEnd : table?.TableCellRightMargin?.Width?.Value is { } tableRight ? tableRight / 20d : 5.4;
        var top = Twips(cell?.TopMargin?.Width?.Value) is > 0 and var cellTop ? cellTop : Twips(table?.TopMargin?.Width?.Value);
        var bottom = Twips(cell?.BottomMargin?.Width?.Value) is > 0 and var cellBottom ? cellBottom : Twips(table?.BottomMargin?.Width?.Value);

        return (top, right, bottom, left);
    }

    private static double Twips(string value)
    {
        // NaN and infinity parse as numbers but are no length; a length past any page is held to a page's size.
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips) && double.IsFinite(twips)
            ? Math.Clamp(twips / 20, -MaxPageSide, MaxPageSide)
            : 0;
    }

    private static (bool FirstRow, bool LastRow, bool FirstColumn, bool LastColumn, bool NoHorizontalBand, bool NoVerticalBand) ReadLook(TableLook look)
    {
        if (look is null)
        {
            return (true, false, true, false, false, true);
        }

        var mask = int.TryParse(look.Val?.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : 0x04A0;

        return (
            look.FirstRow?.Value ?? (mask & 0x0020) != 0,
            look.LastRow?.Value ?? (mask & 0x0040) != 0,
            look.FirstColumn?.Value ?? (mask & 0x0080) != 0,
            look.LastColumn?.Value ?? (mask & 0x0100) != 0,
            look.NoHorizontalBand?.Value ?? (mask & 0x0200) != 0,
            look.NoVerticalBand?.Value ?? (mask & 0x0400) != 0);
    }

    /// <summary>
    /// A laid-out table row.
    /// </summary>
    private sealed class RowLayout
    {
        public TableRow Element { get; set; }

        public int Index { get; set; }

        public int Position { get; set; }

        public bool IsHeader { get; set; }

        public bool IsFirst { get; set; }

        public bool IsLast { get; set; }

        public double Height { get; set; }

        public List<CellLayout> Cells { get; set; } = [];

        public WordBox Box { get; set; }
    }

    /// <summary>
    /// A laid-out table cell.
    /// </summary>
    private sealed class CellLayout
    {
        public TableCell Element { get; set; }

        public double X { get; set; }

        public double Width { get; set; }

        public int Column { get; set; }

        public int Span { get; set; }

        public bool LastColumn { get; set; }

        public WordBox Content { get; set; }

        public (double Top, double Right, double Bottom, double Left) Margins { get; set; }

        public string Fill { get; set; }

        public bool Continues { get; set; }

        public bool Restarts { get; set; }

        public int MergedRows { get; set; } = 1;

        public TableCellBorders Borders { get; set; }

        public string VerticalAlignment { get; set; }
    }
}
