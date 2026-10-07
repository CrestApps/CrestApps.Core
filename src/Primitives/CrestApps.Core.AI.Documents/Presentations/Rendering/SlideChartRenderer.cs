using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;

namespace CrestApps.Core.AI.Documents.Presentations.Rendering;

/// <summary>
/// Draws a chart from the values it caches: axes, gridlines, bars, lines, slices, labels and a legend.
/// </summary>
/// <remarks>
/// PowerPoint draws a chart itself from the same cached values, so the preview is drawn from them too, laid
/// out the way PowerPoint's default chart style lays them out. It is a faithful picture of what the chart
/// says rather than a pixel copy of how PowerPoint styles it.
/// </remarks>
internal static class SlideChartRenderer
{
    private const string GridColor = "D9D9D9";
    private const string AxisColor = "BFBFBF";

    /// <summary>
    /// Draws a chart into a box.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="x">The left edge of the box in points.</param>
    /// <param name="y">The top edge of the box in points.</param>
    /// <param name="w">The width in points.</param>
    /// <param name="h">The height in points.</param>
    /// <param name="font">The typeface of the chart text.</param>
    /// <param name="textColor">The colour of the chart text.</param>
    /// <param name="items">The drawing the chart is added to.</param>
    public static void Draw(PresentationChart chart, double x, double y, double w, double h, string font, string textColor, IList<SlideDrawingItem> items)
    {
        if (w < 20 || h < 20)
        {
            return;
        }

        var size = Math.Clamp(chart.FontSize ?? 12, 7, 18);
        size = Math.Min(size, Math.Max(7, h / 16));
        font = chart.Font ?? font;
        textColor = chart.TextColor ?? textColor;

        var pad = Math.Min(8, w / 30);
        var left = x + pad;
        var top = y + pad;
        var right = x + w - pad;
        var bottom = y + h - pad;

        if (!string.IsNullOrWhiteSpace(chart.Title))
        {
            var titleSize = size * 1.3;
            Text(items, x + (w / 2), top + titleSize, Fit(chart.Title, font, titleSize, false, w - (2 * pad)), font, titleSize, textColor, "middle");
            top += titleSize * 1.7;
        }

        var isPie = chart.Kind is "pie" or "doughnut";
        var entries = isPie
            ? chart.Categories.Select((category, index) => (Name: category, Color: PointColor(chart, index))).ToList()
            : chart.Series.Select((series, index) => (Name: series.Name ?? "Series " + (index + 1), Color: SeriesColor(chart, index))).ToList();

        if (chart.ShowLegend && entries.Count > 0)
        {
            (left, top, right, bottom) = DrawLegend(entries, chart.LegendPosition, left, top, right, bottom, font, size, textColor, items);
        }

        if (right - left < 20 || bottom - top < 20)
        {
            return;
        }

        switch (chart.Kind)
        {
            case "pie":
            case "doughnut":
                DrawPie(chart, left, top, right, bottom, font, size, textColor, items);
                break;

            case "scatter":
            case "bubble":
                DrawScatter(chart, left, top, right, bottom, font, size, textColor, items);
                break;

            case "radar":
                DrawRadar(chart, left, top, right, bottom, font, size, textColor, items);
                break;

            case "column":
            case "bar":
            case "line":
            case "area":
                DrawCategoryChart(chart, left, top, right, bottom, font, size, textColor, items);
                break;

            default:
                Text(items, (left + right) / 2, (top + bottom) / 2, "Chart", font, size, textColor, "middle");
                break;
        }
    }

    private static (double Left, double Top, double Right, double Bottom) DrawLegend(
        List<(string Name, string Color)> entries,
        string position,
        double left,
        double top,
        double right,
        double bottom,
        string font,
        double size,
        string textColor,
        IList<SlideDrawingItem> items)
    {
        var swatch = size * 0.7;
        var gap = size * 0.9;
        var widths = entries.Select(entry => swatch + (size * 0.35) + PresentationTextMeasurer.Measure(entry.Name ?? string.Empty, font, size, false)).ToList();

        if (position is "left" or "right")
        {
            var column = Math.Min(widths.Max(), (right - left) * 0.35);
            var rowHeight = size * 1.5;
            var startY = ((top + bottom) / 2) - (entries.Count * rowHeight / 2);
            var legendX = position == "right" ? right - column : left;

            for (var index = 0; index < entries.Count; index++)
            {
                var rowY = startY + (index * rowHeight);
                Box(items, legendX, rowY + ((rowHeight - swatch) / 2), swatch, swatch, entries[index].Color);
                Text(items, legendX + swatch + (size * 0.35), rowY + (rowHeight * 0.72), Fit(entries[index].Name, font, size, false, column - swatch - (size * 0.35)), font, size, textColor, "start");
            }

            return position == "right" ? (left, top, right - column - gap, bottom) : (left + column + gap, top, right, bottom);
        }

        // Legends above or below the plot wrap onto as many rows as they need, each row centred.
        var available = right - left;
        var rows = new List<List<int>> { new() };
        var rowWidth = 0d;

        for (var index = 0; index < entries.Count; index++)
        {
            var needed = widths[index] + (rows[^1].Count > 0 ? gap : 0);

            if (rows[^1].Count > 0 && rowWidth + needed > available)
            {
                rows.Add([]);
                rowWidth = 0;
                needed = widths[index];
            }

            rows[^1].Add(index);
            rowWidth += needed;
        }

        var lineHeight = size * 1.5;
        var legendHeight = rows.Count * lineHeight;
        var legendTop = position == "top" ? top : bottom - legendHeight;

        for (var row = 0; row < rows.Count; row++)
        {
            var total = rows[row].Sum(index => widths[index]) + (gap * (rows[row].Count - 1));
            var cursor = left + ((available - total) / 2);
            var rowY = legendTop + (row * lineHeight);

            foreach (var index in rows[row])
            {
                Box(items, cursor, rowY + ((lineHeight - swatch) / 2), swatch, swatch, entries[index].Color);
                Text(items, cursor + swatch + (size * 0.35), rowY + (lineHeight * 0.72), entries[index].Name ?? string.Empty, font, size, textColor, "start");
                cursor += widths[index] + gap;
            }
        }

        return position == "top" ? (left, top + legendHeight + (size * 0.4), right, bottom) : (left, top, right, bottom - legendHeight - (size * 0.4));
    }

    private static void DrawCategoryChart(PresentationChart chart, double left, double top, double right, double bottom, string font, double size, string textColor, IList<SlideDrawingItem> items)
    {
        var categoryCount = Math.Max(chart.Categories.Count, chart.Series.Count == 0 ? 0 : chart.Series.Max(series => series.Values.Count));

        if (categoryCount == 0 || chart.Series.Count == 0)
        {
            return;
        }

        var horizontal = chart.Kind == "bar";
        var stacked = chart.Stacked && chart.Kind is "column" or "bar" or "area";
        var percent = chart.PercentStacked;
        var (minimum, maximum) = ValueRange(chart, categoryCount, stacked, percent);
        // PowerPoint picks as many gridlines as the plot has room for: about one every three and a half lines of
        // text up a value axis, and one every eight or so characters along one, since its labels sit side by
        // side. Like Excel it leaves a little headroom, so the tallest bar never touches the top of the plot.
        var span = horizontal ? right - left : bottom - top;
        var intervals = (int)Math.Clamp(span / (size * (horizontal ? 8.5 : 3.6)), 3, 10);

        if (!percent && maximum > 0)
        {
            maximum += (maximum - Math.Min(0, minimum)) * 0.05;
        }

        var (axisMin, axisMax, step) = NiceScale(minimum, maximum, chart.Kind is "column" or "bar" or "area", intervals);
        var format = percent ? "0%" : chart.NumberFormat;
        var valueLabels = new List<(double Value, string Text)>();

        for (var value = axisMin; value <= axisMax + (step / 2); value += step)
        {
            valueLabels.Add((value, SlideNumberFormat.Format(percent ? value / 100 : value, format)));
        }

        var categoryLabels = Enumerable.Range(0, categoryCount).Select(index => index < chart.Categories.Count ? chart.Categories[index] ?? string.Empty : (index + 1).ToString(CultureInfo.InvariantCulture)).ToList();

        double plotLeft;
        double plotRight = right;
        double plotTop = top + (size * 0.5);
        double plotBottom;

        if (horizontal)
        {
            var labelWidth = Math.Min(categoryLabels.Max(label => PresentationTextMeasurer.Measure(label, font, size, false)), (right - left) * 0.3);
            plotLeft = left + labelWidth + (size * 0.6);
            plotBottom = bottom - (size * 1.6);
        }
        else
        {
            var labelWidth = valueLabels.Max(label => PresentationTextMeasurer.Measure(label.Text, font, size, false));
            plotLeft = left + labelWidth + (size * 0.6);
            plotBottom = bottom - (size * 1.7);
        }

        if (!string.IsNullOrWhiteSpace(chart.ValueAxisTitle) && !horizontal)
        {
            plotLeft += size * 1.4;
            Text(items, left + size, (plotTop + plotBottom) / 2, chart.ValueAxisTitle, font, size, textColor, "middle", rotation: -90);
        }

        if (!string.IsNullOrWhiteSpace(chart.CategoryAxisTitle) && !horizontal)
        {
            plotBottom -= size * 1.5;
            Text(items, (plotLeft + plotRight) / 2, bottom - (size * 0.2), chart.CategoryAxisTitle, font, size, textColor, "middle");
        }

        if (plotRight - plotLeft < 10 || plotBottom - plotTop < 10)
        {
            return;
        }

        double ToPosition(double value)
        {
            var fraction = (value - axisMin) / (axisMax - axisMin);

            return horizontal
                ? plotLeft + (fraction * (plotRight - plotLeft))
                : plotBottom - (fraction * (plotBottom - plotTop));
        }

        // Gridlines and value labels.
        foreach (var (value, text) in valueLabels)
        {
            var position = ToPosition(value);

            if (horizontal)
            {
                Line(items, position, plotTop, position, plotBottom, GridColor, 0.75);
                Text(items, position, plotBottom + (size * 1.2), text, font, size, textColor, "middle");
            }
            else
            {
                Line(items, plotLeft, position, plotRight, position, GridColor, 0.75);
                Text(items, plotLeft - (size * 0.4), position + (size * 0.35), text, font, size, textColor, "end");
            }
        }

        var baseline = ToPosition(Math.Clamp(0, axisMin, axisMax));
        var slot = (horizontal ? plotBottom - plotTop : plotRight - plotLeft) / categoryCount;

        // Category labels.
        for (var index = 0; index < categoryCount; index++)
        {
            var center = (horizontal ? plotTop : plotLeft) + (slot * (index + 0.5));

            if (horizontal)
            {
                Text(items, plotLeft - (size * 0.4), center + (size * 0.35), Fit(categoryLabels[index], font, size, false, plotLeft - left - (size * 0.4)), font, size, textColor, "end");
            }
            else if (categoryCount <= 40)
            {
                Text(items, center, plotBottom + (size * 1.25), Fit(categoryLabels[index], font, size, false, Math.Max(slot - 2, size * 2)), font, size, textColor, "middle");
            }
        }

        if (chart.Kind is "column" or "bar")
        {
            DrawBars(chart, categoryCount, horizontal, stacked, percent, slot, plotLeft, plotTop, ToPosition, font, size, textColor, items);
        }
        else
        {
            DrawLines(chart, categoryCount, horizontal, stacked, percent, slot, plotLeft, plotTop, ToPosition, font, size, textColor, items);
        }

        // The axis line sits on top of the bars' feet.
        if (horizontal)
        {
            Line(items, baseline, plotTop, baseline, plotBottom, AxisColor, 0.75);
        }
        else
        {
            Line(items, plotLeft, baseline, plotRight, baseline, AxisColor, 0.75);
        }
    }

    private static void DrawBars(
        PresentationChart chart,
        int categoryCount,
        bool horizontal,
        bool stacked,
        bool percent,
        double slot,
        double plotLeft,
        double plotTop,
        Func<double, double> toPosition,
        string font,
        double size,
        string textColor,
        IList<SlideDrawingItem> items)
    {
        var groupWidth = slot / 1.8;
        var barWidth = stacked ? groupWidth : groupWidth / chart.Series.Count;

        for (var category = 0; category < categoryCount; category++)
        {
            var slotStart = (horizontal ? plotTop : plotLeft) + (slot * category) + ((slot - groupWidth) / 2);
            var positive = 0d;
            var negative = 0d;
            var total = percent ? chart.Series.Sum(series => Math.Abs(Value(series, category) ?? 0)) : 1;

            for (var index = 0; index < chart.Series.Count; index++)
            {
                if (Value(chart.Series[index], category) is not { } raw)
                {
                    continue;
                }

                var value = percent && total > 0 ? raw / total * 100 : raw;
                double from;
                double to;

                if (stacked)
                {
                    if (value >= 0)
                    {
                        from = positive;
                        positive += value;
                        to = positive;
                    }
                    else
                    {
                        from = negative;
                        negative += value;
                        to = negative;
                    }
                }
                else
                {
                    from = 0;
                    to = value;
                }

                var start = toPosition(from);
                var end = toPosition(to);
                var offset = stacked ? slotStart : slotStart + (barWidth * index);
                var color = chart.Series[index].PointColors.Count > category && chart.Series[index].PointColors[category] is { } pointColor
                    ? pointColor
                    : SeriesColor(chart, index);

                if (horizontal)
                {
                    Box(items, Math.Min(start, end), offset, Math.Abs(end - start), barWidth * 0.98, color);
                }
                else
                {
                    Box(items, offset, Math.Min(start, end), barWidth * 0.98, Math.Abs(end - start), color);
                }

                if (chart.ShowDataLabels)
                {
                    var label = SlideNumberFormat.Format(raw, chart.NumberFormat);
                    var labelSize = size * 0.9;

                    if (horizontal)
                    {
                        Text(items, Math.Max(start, end) + (labelSize * 0.3), offset + (barWidth / 2) + (labelSize * 0.35), label, font, labelSize, textColor, "start");
                    }
                    else
                    {
                        Text(items, offset + (barWidth / 2), Math.Min(start, end) - (labelSize * 0.3), label, font, labelSize, textColor, "middle");
                    }
                }
            }
        }
    }

    private static void DrawLines(
        PresentationChart chart,
        int categoryCount,
        bool horizontal,
        bool stacked,
        bool percent,
        double slot,
        double plotLeft,
        double plotTop,
        Func<double, double> toPosition,
        string font,
        double size,
        string textColor,
        IList<SlideDrawingItem> items)
    {
        var running = new double[categoryCount];
        var isArea = chart.Kind == "area";

        for (var index = 0; index < chart.Series.Count; index++)
        {
            var series = chart.Series[index];
            var color = SeriesColor(chart, index);
            var points = new List<(double X, double Y)>();
            var lower = new List<(double X, double Y)>();

            for (var category = 0; category < categoryCount; category++)
            {
                if (Value(series, category) is not { } raw)
                {
                    continue;
                }

                var total = percent ? chart.Series.Sum(other => Math.Abs(Value(other, category) ?? 0)) : 1;
                var value = percent && total > 0 ? raw / total * 100 : raw;
                var from = stacked ? running[category] : 0;
                var to = stacked ? running[category] + value : value;

                if (stacked)
                {
                    running[category] = to;
                }

                var along = (horizontal ? plotTop : plotLeft) + (slot * (category + 0.5));
                points.Add(horizontal ? (toPosition(to), along) : (along, toPosition(to)));
                lower.Add(horizontal ? (toPosition(from), along) : (along, toPosition(from)));
            }

            if (points.Count == 0)
            {
                continue;
            }

            if (isArea)
            {
                var outline = points.Concat(Enumerable.Reverse(lower)).ToArray();
                items.Add(new SlideShapeDrawing { Paths = [SlideGeometry.Polygon(outline)], Fill = SlidePaint.Solid(color, 0.85) });
            }
            else
            {
                var path = new SlidePath { Filled = false };

                for (var point = 0; point < points.Count; point++)
                {
                    path.Commands.Add(point == 0 ? SlidePathCommand.MoveTo(points[point].X, points[point].Y) : SlidePathCommand.LineTo(points[point].X, points[point].Y));
                }

                items.Add(new SlideShapeDrawing { Paths = [path], Stroke = new SlideStroke { Color = color, Width = 2.25 } });

                foreach (var (px, py) in points)
                {
                    items.Add(new SlideShapeDrawing { Paths = [SlideGeometry.Ellipse(px, py, 2.6, 2.6)], Fill = SlidePaint.Solid(color) });
                }
            }

            if (chart.ShowDataLabels)
            {
                for (var point = 0; point < points.Count; point++)
                {
                    var raw = series.Values.Where(value => value is not null).ElementAtOrDefault(point);

                    if (raw is { } value)
                    {
                        Text(items, points[point].X, points[point].Y - (size * 0.5), SlideNumberFormat.Format(value, chart.NumberFormat), font, size * 0.9, textColor, "middle");
                    }
                }
            }
        }
    }

    private static void DrawPie(PresentationChart chart, double left, double top, double right, double bottom, string font, double size, string textColor, IList<SlideDrawingItem> items)
    {
        if (chart.Series.Count == 0)
        {
            return;
        }

        var series = chart.Series[0];
        var values = series.Values.Select(value => Math.Max(0, value ?? 0)).ToList();
        var total = values.Sum();

        if (total <= 0)
        {
            return;
        }

        var radius = Math.Min(right - left, bottom - top) / 2 * 0.92;
        var cx = (left + right) / 2;
        var cy = (top + bottom) / 2;
        var hole = chart.Kind == "doughnut" ? 0.55 : 0;
        var angle = -90d;

        for (var index = 0; index < values.Count; index++)
        {
            var sweep = values[index] / total * 360;

            if (sweep <= 0)
            {
                continue;
            }

            var slice = SlideGeometry.Slice(cx, cy, radius, radius, angle, sweep, hole);

            items.Add(new SlideShapeDrawing
            {
                Paths = [slice],
                Fill = SlidePaint.Solid(PointColor(chart, index)),
                Stroke = new SlideStroke { Color = "FFFFFF", Width = 1.5 },
            });

            if (chart.ShowDataLabels)
            {
                var middle = (angle + (sweep / 2)) * Math.PI / 180;
                var distance = radius * (hole > 0 ? (1 + hole) / 2 : 0.66);
                var label = string.IsNullOrWhiteSpace(chart.NumberFormat) || chart.NumberFormat.Contains('%')
                    ? SlideNumberFormat.Format(values[index] / total, "0%")
                    : SlideNumberFormat.Format(values[index], chart.NumberFormat);
                var labelColor = PresentationColorContrast(PointColor(chart, index));

                Text(items, cx + (Math.Cos(middle) * distance), cy + (Math.Sin(middle) * distance) + (size * 0.35), label, font, size, labelColor, "middle", bold: true);
            }

            angle += sweep;
        }
    }

    private static void DrawScatter(PresentationChart chart, double left, double top, double right, double bottom, string font, double size, string textColor, IList<SlideDrawingItem> items)
    {
        var xs = chart.Series.SelectMany(series => series.XValues.Count > 0 ? series.XValues : series.Values.Select((_, index) => (double?)(index + 1))).Where(value => value is not null).Select(value => value.Value).ToList();
        var ys = chart.Series.SelectMany(series => series.Values).Where(value => value is not null).Select(value => value.Value).ToList();

        if (xs.Count == 0 || ys.Count == 0)
        {
            return;
        }

        var (xMin, xMax, xStep) = NiceScale(xs.Min(), xs.Max(), false);
        var (yMin, yMax, yStep) = NiceScale(ys.Min(), ys.Max(), false);
        var labelWidth = PresentationTextMeasurer.Measure(SlideNumberFormat.Format(yMax, chart.NumberFormat), font, size, false);
        var plotLeft = left + labelWidth + (size * 0.6);
        var plotBottom = bottom - (size * 1.7);
        var plotTop = top + (size * 0.5);

        double X(double value) => plotLeft + ((value - xMin) / (xMax - xMin) * (right - plotLeft));

        double Y(double value) => plotBottom - ((value - yMin) / (yMax - yMin) * (plotBottom - plotTop));

        for (var value = yMin; value <= yMax + (yStep / 2); value += yStep)
        {
            Line(items, plotLeft, Y(value), right, Y(value), GridColor, 0.75);
            Text(items, plotLeft - (size * 0.4), Y(value) + (size * 0.35), SlideNumberFormat.Format(value, chart.NumberFormat), font, size, textColor, "end");
        }

        for (var value = xMin; value <= xMax + (xStep / 2); value += xStep)
        {
            Text(items, X(value), plotBottom + (size * 1.25), SlideNumberFormat.General(value), font, size, textColor, "middle");
        }

        Line(items, plotLeft, plotBottom, right, plotBottom, AxisColor, 0.75);

        for (var index = 0; index < chart.Series.Count; index++)
        {
            var series = chart.Series[index];
            var color = SeriesColor(chart, index);

            for (var point = 0; point < series.Values.Count; point++)
            {
                var xValue = series.XValues.Count > point ? series.XValues[point] : point + 1;

                if (xValue is { } px && series.Values[point] is { } py)
                {
                    items.Add(new SlideShapeDrawing { Paths = [SlideGeometry.Ellipse(X(px), Y(py), 3.2, 3.2)], Fill = SlidePaint.Solid(color) });
                }
            }
        }
    }

    private static void DrawRadar(PresentationChart chart, double left, double top, double right, double bottom, string font, double size, string textColor, IList<SlideDrawingItem> items)
    {
        var count = chart.Categories.Count > 0 ? chart.Categories.Count : chart.Series.Count == 0 ? 0 : chart.Series.Max(series => series.Values.Count);

        if (count < 3)
        {
            return;
        }

        var maximum = chart.Series.SelectMany(series => series.Values).Where(value => value is not null).Select(value => value.Value).DefaultIfEmpty(1).Max();
        var (_, axisMax, _) = NiceScale(0, maximum, true);
        var radius = (Math.Min(right - left, bottom - top) / 2) - (size * 1.5);
        var cx = (left + right) / 2;
        var cy = (top + bottom) / 2;

        (double X, double Y) Point(int index, double fraction)
        {
            var angle = (-Math.PI / 2) + (index * 2 * Math.PI / count);

            return (cx + (Math.Cos(angle) * radius * fraction), cy + (Math.Sin(angle) * radius * fraction));
        }

        for (var ring = 1; ring <= 4; ring++)
        {
            var web = SlideGeometry.Polygon(Enumerable.Range(0, count).Select(index => Point(index, ring / 4d)).ToArray());
            web.Filled = false;
            items.Add(new SlideShapeDrawing { Paths = [web], Stroke = new SlideStroke { Color = GridColor, Width = 0.75 } });
        }

        for (var index = 0; index < count; index++)
        {
            var (px, py) = Point(index, 1.12);
            var label = index < chart.Categories.Count ? chart.Categories[index] : (index + 1).ToString(CultureInfo.InvariantCulture);
            Text(items, px, py + (size * 0.35), label, font, size, textColor, "middle");
        }

        for (var index = 0; index < chart.Series.Count; index++)
        {
            var series = chart.Series[index];
            var outline = SlideGeometry.Polygon(Enumerable.Range(0, count).Select(point => Point(point, Math.Max(0, Value(series, point) ?? 0) / axisMax)).ToArray());
            outline.Filled = false;
            items.Add(new SlideShapeDrawing { Paths = [outline], Stroke = new SlideStroke { Color = SeriesColor(chart, index), Width = 2 } });
        }
    }

    private static (double Minimum, double Maximum) ValueRange(PresentationChart chart, int categoryCount, bool stacked, bool percent)
    {
        if (percent)
        {
            return (0, 100);
        }

        var minimum = 0d;
        var maximum = 0d;

        if (stacked)
        {
            for (var category = 0; category < categoryCount; category++)
            {
                var positive = chart.Series.Sum(series => Math.Max(0, Value(series, category) ?? 0));
                var negative = chart.Series.Sum(series => Math.Min(0, Value(series, category) ?? 0));
                maximum = Math.Max(maximum, positive);
                minimum = Math.Min(minimum, negative);
            }

            return (minimum, maximum);
        }

        var values = chart.Series.SelectMany(series => series.Values).Where(value => value is not null).Select(value => value.Value).ToList();

        if (values.Count == 0)
        {
            return (0, 1);
        }

        minimum = values.Min();
        maximum = values.Max();

        // Lines start from zero too, unless the values sit far above it, where PowerPoint lets the axis float.
        if (chart.Kind != "line" || (minimum >= 0 && minimum < maximum * 0.6))
        {
            minimum = Math.Min(0, minimum);
        }

        return (minimum, maximum);
    }

    /// <summary>
    /// Chooses an axis range and step made of round numbers, the way chart axes are labelled.
    /// </summary>
    /// <param name="minimum">The smallest value to show.</param>
    /// <param name="maximum">The largest value to show.</param>
    /// <param name="includeZero">Whether the range must include zero.</param>
    /// <param name="intervals">About how many steps the axis should have.</param>
    /// <returns>The axis minimum, maximum and step.</returns>
    public static (double Minimum, double Maximum, double Step) NiceScale(double minimum, double maximum, bool includeZero, int intervals = 5)
    {
        if (includeZero)
        {
            minimum = Math.Min(0, minimum);
            maximum = Math.Max(0, maximum);
        }

        if (maximum - minimum < 1e-12)
        {
            maximum = minimum + (Math.Abs(minimum) < 1e-12 ? 1 : Math.Abs(minimum) * 0.1);
        }

        var step = NiceNumber((maximum - minimum) / Math.Max(1, intervals), round: true);
        var niceMinimum = Math.Floor(minimum / step) * step;
        var niceMaximum = Math.Ceiling(maximum / step) * step;

        if (niceMaximum - niceMinimum < step)
        {
            niceMaximum = niceMinimum + step;
        }

        return (niceMinimum, niceMaximum, step);
    }

    private static double NiceNumber(double value, bool round)
    {
        var exponent = Math.Floor(Math.Log10(value));
        var fraction = value / Math.Pow(10, exponent);
        double nice;

        if (round)
        {
            nice = fraction < 1.5 ? 1 : fraction < 3 ? 2 : fraction < 7 ? 5 : 10;
        }
        else
        {
            nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        }

        return nice * Math.Pow(10, exponent);
    }

    private static double? Value(PresentationChartSeries series, int index)
    {
        return index < series.Values.Count ? series.Values[index] : null;
    }

    private static string SeriesColor(PresentationChart chart, int index)
    {
        var fallback = (index % 6) switch
        {
            0 => "4472C4",
            1 => "ED7D31",
            2 => "A5A5A5",
            3 => "FFC000",
            4 => "5B9BD5",
            _ => "70AD47",
        };

        return index < chart.Series.Count ? chart.Series[index].Color ?? fallback : fallback;
    }

    private static string PointColor(PresentationChart chart, int index)
    {
        if (chart.Series.Count > 0 && index < chart.Series[0].PointColors.Count && chart.Series[0].PointColors[index] is { } color)
        {
            return color;
        }

        return SeriesColor(chart, index);
    }

    private static string PresentationColorContrast(string background)
    {
        return PresentationColor.RelativeLuminance(background) > 0.5 ? "262626" : "FFFFFF";
    }

    private static string Fit(string text, string font, double size, bool bold, double width)
    {
        if (string.IsNullOrEmpty(text) || PresentationTextMeasurer.Measure(text, font, size, bold) <= width)
        {
            return text ?? string.Empty;
        }

        for (var length = text.Length - 1; length > 0; length--)
        {
            var candidate = text[..length].TrimEnd() + "…";

            if (PresentationTextMeasurer.Measure(candidate, font, size, bold) <= width)
            {
                return candidate;
            }
        }

        return "…";
    }

    private static void Box(IList<SlideDrawingItem> items, double x, double y, double w, double h, string color)
    {
        if (w <= 0 || h <= 0)
        {
            return;
        }

        items.Add(new SlideShapeDrawing { Paths = [SlideGeometry.Rectangle(x, y, w, h)], Fill = SlidePaint.Solid(color) });
    }

    private static void Line(IList<SlideDrawingItem> items, double x1, double y1, double x2, double y2, string color, double width)
    {
        items.Add(new SlideShapeDrawing
        {
            Paths = [new SlidePath { Filled = false, Commands = [SlidePathCommand.MoveTo(x1, y1), SlidePathCommand.LineTo(x2, y2)] }],
            Stroke = new SlideStroke { Color = color, Width = width },
        });
    }

    private static void Text(IList<SlideDrawingItem> items, double x, double baseline, string text, string font, double size, string color, string anchor, double rotation = 0, bool bold = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var width = PresentationTextMeasurer.Measure(text, font, size, bold);

        items.Add(new SlideTextDrawing
        {
            Rotation = rotation,
            CenterX = x,
            CenterY = baseline - (size * 0.35),
            Lines =
            [
                new SlideTextLine
                {
                    X = x,
                    Baseline = baseline,
                    Anchor = anchor,
                    Width = width,
                    Runs = [new SlideTextRun { Text = text, Font = font, Size = size, Color = color, Bold = bold, Width = width }],
                },
            ],
        });
    }
}
