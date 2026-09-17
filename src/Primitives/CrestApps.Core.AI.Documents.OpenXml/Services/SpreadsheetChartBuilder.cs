using System.Globalization;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace CrestApps.Core.AI.Documents.OpenXml.Services;

/// <summary>
/// Embeds native charts in a generated worksheet.
/// <para>
/// The chart is bound to the worksheet's own cell ranges rather than to a snapshot of the numbers, so
/// it redraws itself when the reader edits, sorts, or filters the data. A cached copy of the plotted
/// values is written alongside the ranges so the chart still renders in viewers that do not recalculate
/// on open.
/// </para>
/// </summary>
internal static class SpreadsheetChartBuilder
{
    private const long EmusPerPixel = 9525;
    private const int DefaultChartWidth = 720;
    private const int DefaultChartHeight = 400;

    // Charts are anchored to the right of the data so they never sit on top of the rows or the total.
    private const int ColumnGapAfterData = 1;
    private const int RowsPerChart = 21;

    /// <summary>
    /// Builds the chart parts for a layout and returns the drawing element that anchors them to the
    /// worksheet.
    /// </summary>
    /// <param name="layout">The resolved sheet layout.</param>
    /// <param name="worksheetPart">The worksheet part the charts are added to.</param>
    /// <returns>
    /// The drawing element to append to the worksheet, or <see langword="null"/> when there is nothing
    /// chartable.
    /// </returns>
    public static Drawing TryBuildCharts(SpreadsheetLayout layout, WorksheetPart worksheetPart)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(worksheetPart);

        var charts = layout.Formatting.Charts;

        if (charts is null || charts.Count == 0 || layout.Rows.Count == 0)
        {
            return null;
        }

        DrawingsPart drawingsPart = null;
        Xdr.WorksheetDrawing worksheetDrawing = null;
        var anchoredCount = 0;

        foreach (var chart in charts)
        {
            var plan = ChartPlan.TryCreate(chart, layout);

            if (plan is null)
            {
                continue;
            }

            // The drawing part is only created once something is actually chartable, so a spec naming
            // columns that do not exist leaves no empty drawing behind.
            if (drawingsPart is null)
            {
                drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
                worksheetDrawing = new Xdr.WorksheetDrawing();
            }

            var chartPart = drawingsPart.AddNewPart<ChartPart>();
            chartPart.ChartSpace = BuildChartSpace(plan, layout);
            chartPart.ChartSpace.Save();

            worksheetDrawing.Append(BuildAnchor(
                drawingsPart.GetIdOfPart(chartPart),
                plan,
                layout,
                anchoredCount++));
        }

        if (drawingsPart is null)
        {
            return null;
        }

        drawingsPart.WorksheetDrawing = worksheetDrawing;
        drawingsPart.WorksheetDrawing.Save();

        return new Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) };
    }

    private static Xdr.OneCellAnchor BuildAnchor(
        string relationshipId,
        ChartPlan plan,
        SpreadsheetLayout layout,
        int chartIndex)
    {
        var column = layout.Columns.Count + ColumnGapAfterData;
        var row = chartIndex * RowsPerChart;

        return new Xdr.OneCellAnchor(
            new Xdr.FromMarker(
                new Xdr.ColumnId(column.ToString(CultureInfo.InvariantCulture)),
                new Xdr.ColumnOffset("0"),
                new Xdr.RowId(row.ToString(CultureInfo.InvariantCulture)),
                new Xdr.RowOffset("0")),
            new Xdr.Extent
            {
                Cx = plan.Width * EmusPerPixel,
                Cy = plan.Height * EmusPerPixel,
            },
            new Xdr.GraphicFrame(
                new Xdr.NonVisualGraphicFrameProperties(
                    new Xdr.NonVisualDrawingProperties
                    {
                        Id = (uint)(chartIndex + 2),
                        Name = "Chart " + (chartIndex + 1).ToString(CultureInfo.InvariantCulture),
                    },
                    new Xdr.NonVisualGraphicFrameDrawingProperties()),
                new Xdr.Transform(
                    new A.Offset { X = 0L, Y = 0L },
                    new A.Extents
                    {
                        Cx = plan.Width * EmusPerPixel,
                        Cy = plan.Height * EmusPerPixel,
                    }),
                new A.Graphic(new A.GraphicData(new C.ChartReference { Id = relationshipId })
                {
                    Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart",
                })),
            new Xdr.ClientData());
    }

    private static C.ChartSpace BuildChartSpace(ChartPlan plan, SpreadsheetLayout layout)
    {
        var plotArea = new C.PlotArea(new C.Layout());
        var categoryAxisId = 111111111U;
        var valueAxisId = 222222222U;

        plotArea.Append(BuildPlot(plan, layout, categoryAxisId, valueAxisId));

        if (plan.Kind != SpreadsheetChartKind.Pie)
        {
            plotArea.Append(BuildCategoryAxis(categoryAxisId, valueAxisId));
            plotArea.Append(BuildValueAxis(plan, categoryAxisId, valueAxisId));
        }

        var chart = new C.Chart();

        if (!string.IsNullOrWhiteSpace(plan.Title))
        {
            chart.Append(BuildTitle(plan.Title));
            chart.Append(new C.AutoTitleDeleted { Val = false });
        }
        else
        {
            chart.Append(new C.AutoTitleDeleted { Val = true });
        }

        chart.Append(plotArea);

        // A single series needs no legend; more than one does, or the reader cannot tell them apart.
        if (plan.Series.Count > 1 || plan.Kind == SpreadsheetChartKind.Pie)
        {
            chart.Append(new C.Legend(
                new C.LegendPosition { Val = C.LegendPositionValues.Bottom },
                new C.Overlay { Val = false }));
        }

        chart.Append(new C.PlotVisibleOnly { Val = true });

        return new C.ChartSpace(
            new C.EditingLanguage { Val = "en-US" },
            chart);
    }

    private static OpenXmlCompositeElement BuildPlot(
        ChartPlan plan,
        SpreadsheetLayout layout,
        uint categoryAxisId,
        uint valueAxisId)
    {
        switch (plan.Kind)
        {
            case SpreadsheetChartKind.Pie:
                var pie = new C.PieChart(new C.VaryColors { Val = true });
                pie.Append(BuildPieSeries(plan, layout));

                return pie;

            case SpreadsheetChartKind.Line:
                var line = new C.LineChart(
                    new C.Grouping { Val = C.GroupingValues.Standard },
                    new C.VaryColors { Val = false });

                AppendSeries(line, plan, layout, CreateLineSeries);
                line.Append(new C.AxisId { Val = categoryAxisId });
                line.Append(new C.AxisId { Val = valueAxisId });

                return line;

            case SpreadsheetChartKind.Area:
                var area = new C.AreaChart(
                    new C.Grouping { Val = C.GroupingValues.Standard },
                    new C.VaryColors { Val = false });

                AppendSeries(area, plan, layout, CreateAreaSeries);
                area.Append(new C.AxisId { Val = categoryAxisId });
                area.Append(new C.AxisId { Val = valueAxisId });

                return area;

            default:
                var bar = new C.BarChart(
                    new C.BarDirection
                    {
                        Val = plan.Kind == SpreadsheetChartKind.Bar
                            ? C.BarDirectionValues.Bar
                            : C.BarDirectionValues.Column,
                    },
                    new C.BarGrouping { Val = C.BarGroupingValues.Clustered },
                    new C.VaryColors { Val = false });

                AppendSeries(bar, plan, layout, CreateBarSeries);
                bar.Append(new C.AxisId { Val = categoryAxisId });
                bar.Append(new C.AxisId { Val = valueAxisId });

                return bar;
        }
    }

    private static void AppendSeries(
        OpenXmlCompositeElement plot,
        ChartPlan plan,
        SpreadsheetLayout layout,
        Func<ChartPlan, SpreadsheetLayout, SpreadsheetLayoutColumn, int, OpenXmlCompositeElement> factory)
    {
        for (var index = 0; index < plan.Series.Count; index++)
        {
            plot.Append(factory(plan, layout, plan.Series[index], index));
        }
    }

    private static OpenXmlCompositeElement CreateBarSeries(
        ChartPlan plan,
        SpreadsheetLayout layout,
        SpreadsheetLayoutColumn column,
        int index)
    {
        return new C.BarChartSeries(BuildSeriesParts(plan, layout, column, index));
    }

    private static OpenXmlCompositeElement CreateLineSeries(
        ChartPlan plan,
        SpreadsheetLayout layout,
        SpreadsheetLayoutColumn column,
        int index)
    {
        var series = new C.LineChartSeries(BuildSeriesParts(plan, layout, column, index));
        series.Append(new C.Marker(new C.Symbol { Val = C.MarkerStyleValues.None }));

        return series;
    }

    private static OpenXmlCompositeElement CreateAreaSeries(
        ChartPlan plan,
        SpreadsheetLayout layout,
        SpreadsheetLayoutColumn column,
        int index)
    {
        return new C.AreaChartSeries(BuildSeriesParts(plan, layout, column, index));
    }

    private static C.PieChartSeries BuildPieSeries(ChartPlan plan, SpreadsheetLayout layout)
    {
        return new C.PieChartSeries(BuildSeriesParts(plan, layout, plan.Series[0], 0));
    }

    private static OpenXmlElement[] BuildSeriesParts(
        ChartPlan plan,
        SpreadsheetLayout layout,
        SpreadsheetLayoutColumn column,
        int index)
    {
        var sheet = QuoteSheetName(layout.SheetName);
        var headerCell = $"{sheet}!${SpreadsheetFormula.ToColumnName(column.Index)}${SpreadsheetLayout.HeaderRowNumber.ToString(CultureInfo.InvariantCulture)}";

        return
        [
            new C.Index { Val = (uint)index },
            new C.Order { Val = (uint)index },
            new C.SeriesText(new C.StringReference(
                new C.Formula(headerCell),
                new C.StringCache(
                    new C.PointCount { Val = 1U },
                    new C.StringPoint(new C.NumericValue(column.Name ?? string.Empty)) { Index = 0U }))),
            BuildCategoryAxisData(plan, layout),
            BuildValues(plan, layout, column),
        ];
    }

    private static C.CategoryAxisData BuildCategoryAxisData(ChartPlan plan, SpreadsheetLayout layout)
    {
        var cache = new C.StringCache(new C.PointCount { Val = (uint)plan.RowCount });

        for (var index = 0; index < plan.RowCount; index++)
        {
            var row = layout.Rows[index];
            var value = row is not null && plan.CategoryColumn.Index < row.Count
                ? row[plan.CategoryColumn.Index]
                : string.Empty;

            cache.Append(new C.StringPoint(new C.NumericValue(value ?? string.Empty))
            {
                Index = (uint)index,
            });
        }

        return new C.CategoryAxisData(new C.StringReference(
            new C.Formula(BuildRange(layout, plan.CategoryColumn.Index, plan.RowCount)),
            cache));
    }

    private static C.Values BuildValues(ChartPlan plan, SpreadsheetLayout layout, SpreadsheetLayoutColumn column)
    {
        var cache = new C.NumberingCache(new C.PointCount { Val = (uint)plan.RowCount });

        for (var index = 0; index < plan.RowCount; index++)
        {
            var row = layout.Rows[index];
            var raw = row is not null && column.Index < row.Count
                ? row[column.Index]
                : null;

            if (!SpreadsheetValue.TryParseNumber(raw, out var number))
            {
                continue;
            }

            cache.Append(new C.NumericPoint(new C.NumericValue(SpreadsheetNumberFormatCode.ToInvariant(number)))
            {
                Index = (uint)index,
            });
        }

        return new C.Values(new C.NumberReference(
            new C.Formula(BuildRange(layout, column.Index, plan.RowCount)),
            cache));
    }

    private static string BuildRange(SpreadsheetLayout layout, int columnIndex, int rowCount)
    {
        var sheet = QuoteSheetName(layout.SheetName);
        var name = SpreadsheetFormula.ToColumnName(columnIndex);
        var first = SpreadsheetLayout.FirstDataRowNumber;
        var last = first + rowCount - 1;

        return $"{sheet}!${name}${first.ToString(CultureInfo.InvariantCulture)}:${name}${last.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string QuoteSheetName(string sheetName)
    {
        var name = string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : sheetName;

        // A sheet name containing spaces or punctuation has to be quoted inside a formula, and an
        // apostrophe in the name is escaped by doubling it.
        foreach (var character in name)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                return "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'";
            }
        }

        return name;
    }

    private static C.CategoryAxis BuildCategoryAxis(uint categoryAxisId, uint valueAxisId)
    {
        return new C.CategoryAxis(
            new C.AxisId { Val = categoryAxisId },
            new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
            new C.Delete { Val = false },
            new C.AxisPosition { Val = C.AxisPositionValues.Bottom },
            new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
            new C.CrossingAxis { Val = valueAxisId });
    }

    private static C.ValueAxis BuildValueAxis(ChartPlan plan, uint categoryAxisId, uint valueAxisId)
    {
        var axis = new C.ValueAxis(
            new C.AxisId { Val = valueAxisId },
            new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
            new C.Delete { Val = false },
            new C.AxisPosition { Val = C.AxisPositionValues.Left });

        if (!string.IsNullOrWhiteSpace(plan.ValueNumberFormat))
        {
            // Carrying the column's number format onto the axis keeps a currency chart reading in
            // currency instead of reverting to bare numbers on the axis labels.
            axis.Append(new C.NumberingFormat
            {
                FormatCode = plan.ValueNumberFormat,
                SourceLinked = false,
            });
        }

        axis.Append(new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo });
        axis.Append(new C.CrossingAxis { Val = categoryAxisId });

        return axis;
    }

    private static C.Title BuildTitle(string title)
    {
        return new C.Title(
            new C.ChartText(new C.RichText(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.Run(new A.Text(title))))),
            new C.Overlay { Val = false });
    }

    /// <summary>
    /// A chart request that has been validated against the sheet: the columns exist, at least one
    /// series is plottable, and the number of categories has been capped.
    /// </summary>
    private sealed class ChartPlan
    {
        private ChartPlan(
            SpreadsheetChartKind kind,
            string title,
            SpreadsheetLayoutColumn categoryColumn,
            IReadOnlyList<SpreadsheetLayoutColumn> series,
            int rowCount,
            int width,
            int height,
            string valueNumberFormat)
        {
            Kind = kind;
            Title = title;
            CategoryColumn = categoryColumn;
            Series = series;
            RowCount = rowCount;
            Width = width;
            Height = height;
            ValueNumberFormat = valueNumberFormat;
        }

        public SpreadsheetChartKind Kind { get; }

        public string Title { get; }

        public SpreadsheetLayoutColumn CategoryColumn { get; }

        public IReadOnlyList<SpreadsheetLayoutColumn> Series { get; }

        public int RowCount { get; }

        public int Width { get; }

        public int Height { get; }

        public string ValueNumberFormat { get; }

        public static ChartPlan TryCreate(SpreadsheetChart chart, SpreadsheetLayout layout)
        {
            if (chart is null)
            {
                return null;
            }

            if (layout.Columns.Count == 0)
            {
                return null;
            }

            // With no usable category column the first column is the sensible label source, and it is
            // what a reader would pick by hand.
            var categoryColumn = layout.FindColumn(chart.CategoryColumn) ?? layout.Columns[0];

            var series = new List<SpreadsheetLayoutColumn>();

            if (chart.ValueColumns is not null)
            {
                foreach (var name in chart.ValueColumns)
                {
                    var column = layout.FindColumn(name);

                    if (column is not null && column.Index != categoryColumn.Index)
                    {
                        series.Add(column);
                    }
                }
            }

            if (series.Count == 0)
            {
                // Fall back to every numeric column, so a chart request that names no series still
                // plots something meaningful rather than failing.
                foreach (var column in layout.Columns)
                {
                    if (column.Index != categoryColumn.Index && column.Kind == SpreadsheetDataKind.Number)
                    {
                        series.Add(column);
                    }
                }
            }

            if (series.Count == 0)
            {
                return null;
            }

            if (chart.Kind == SpreadsheetChartKind.Pie && series.Count > 1)
            {
                series.RemoveRange(1, series.Count - 1);
            }

            var maxCategories = chart.MaxCategories is > 0
                ? chart.MaxCategories.Value
                : SpreadsheetFormatting.DefaultMaxChartCategories;

            var rowCount = Math.Min(layout.Rows.Count, maxCategories);

            if (rowCount == 0)
            {
                return null;
            }

            return new ChartPlan(
                chart.Kind,
                chart.Title,
                categoryColumn,
                series,
                rowCount,
                chart.Width is > 0 ? chart.Width.Value : DefaultChartWidth,
                chart.Height is > 0 ? chart.Height.Value : DefaultChartHeight,
                series[0].NumberFormatCode);
        }
    }
}
