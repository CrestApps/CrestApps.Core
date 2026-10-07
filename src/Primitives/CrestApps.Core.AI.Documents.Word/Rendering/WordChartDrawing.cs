using System.Globalization;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Documents.Word.Charts;
using DocumentFormat.OpenXml;

namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// Draws a chart from the data it caches: columns, bars, lines, areas, pies, doughnuts and scatter plots, with
/// a title, axes, gridlines, labels and a legend.
/// </summary>
internal static class WordChartDrawing
{
    private const string AxisColor = "595959";
    private const string GridColor = "E7E6E6";

    // The most gridlines an axis draws, and the largest value it scales to.
    private const int MaxTicks = 100;
    private const double MaxValue = 1e100;

    // A label longer than this is cut before it is measured; no chart has room to draw more.
    private const int MaxLabelLength = 256;

    /// <summary>
    /// Draws a chart into a box of the given size.
    /// </summary>
    /// <param name="spec">The chart.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points.</param>
    /// <param name="source">The block the chart belongs to.</param>
    /// <returns>The items, positioned from the box's top-left corner.</returns>
    public static List<WordDrawItem> Draw(WordChartSpec spec, double width, double height, OpenXmlElement source)
    {
        var items = new List<WordDrawItem>
        {
            new WordRectItem { X = 0, Y = 0, Width = width, Height = height, Fill = "FFFFFF", Stroke = "D9D9D9", StrokeWidth = 0.75, Source = source },
        };

        var top = 8d;

        if (!string.IsNullOrWhiteSpace(spec.Title))
        {
            items.Add(Text(spec.Title, width / 2, 20, 11, bold: true, "262626", source, anchor: "middle", width));
            top = 30;
        }

        var series = spec.Series.Where(item => item.Values.Count > 0).ToList();

        if (series.Count == 0)
        {
            items.Add(Text("(no data)", width / 2, height / 2, 9, bold: false, AxisColor, source, anchor: "middle", width));

            return items;
        }

        var legendItems = spec.Kind is "pie" or "doughnut"
            ? spec.Labels.Select((label, index) => (Label: label, Color: WordChartWriter.PointColor(spec, index))).ToList()
            : series.Select((item, index) => (Label: item.Name ?? "Series " + (index + 1).ToString(CultureInfo.InvariantCulture), Color: WordChartWriter.SeriesColor(spec, spec.Series.IndexOf(item)))).ToList();

        var legend = (spec.Legend ?? "bottom").Trim().ToLowerInvariant();
        var bottom = height - 8;
        var right = width - 10;
        var left = 10d;

        if (legend != "none" && legendItems.Count > 0)
        {
            if (legend is "right" or "left")
            {
                var legendWidth = Math.Min(width * 0.3, legendItems.Max(item => Measure(item.Label, 8)) + 22);
                var legendX = legend == "right" ? width - legendWidth - 6 : 6;
                var y = Math.Max(top, (height - (legendItems.Count * 14)) / 2);

                foreach (var (label, color) in legendItems.Take(20))
                {
                    items.Add(new WordRectItem { X = legendX, Y = y - 7, Width = 8, Height = 8, Fill = color, Source = source });
                    items.Add(Text(Clip(label, legendWidth - 14, 8), legendX + 12, y, 8, bold: false, AxisColor, source));
                    y += 14;
                }

                if (legend == "right")
                {
                    right = legendX - 8;
                }
                else
                {
                    left = legendX + legendWidth + 4;
                }
            }
            else
            {
                var rowY = legend == "top" ? top + 4 : height - 10;
                var totalWidth = legendItems.Take(12).Sum(item => Measure(item.Label, 8) + 24);
                var x = Math.Max(6, (width - totalWidth) / 2);

                foreach (var (label, color) in legendItems.Take(12))
                {
                    items.Add(new WordRectItem { X = x, Y = rowY - 7, Width = 8, Height = 8, Fill = color, Source = source });
                    items.Add(Text(label, x + 12, rowY, 8, bold: false, AxisColor, source));
                    x += Measure(label, 8) + 24;
                }

                if (legend == "top")
                {
                    top += 16;
                }
                else
                {
                    bottom = height - 22;
                }
            }
        }

        switch (spec.Kind)
        {
            case "pie":
            case "doughnut":
                DrawPie(spec, series[0], items, left, top, right, bottom, source);

                break;

            default:
                DrawAxes(spec, series, items, left, top, right, bottom, source);

                break;
        }

        return items;
    }

    private static void DrawPie(WordChartSpec spec, WordChartSeries series, List<WordDrawItem> items, double left, double top, double right, double bottom, OpenXmlElement source)
    {
        var values = series.Values.Select(value => Math.Max(0, Clean(value ?? 0))).ToList();
        var total = values.Sum();

        if (total <= 0)
        {
            return;
        }

        var radius = Math.Max(10, Math.Min(right - left, bottom - top) / 2 - 4);
        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;
        var angle = -Math.PI / 2;

        for (var index = 0; index < values.Count; index++)
        {
            var sweep = values[index] / total * Math.PI * 2;

            if (sweep <= 0)
            {
                continue;
            }

            var slice = new WordPolygonItem { Fill = WordChartWriter.PointColor(spec, index), Stroke = "FFFFFF", StrokeWidth = 1, Source = source };

            slice.Points.Add((centerX, centerY));

            var steps = Math.Max(2, (int)(sweep / 0.05));

            for (var step = 0; step <= steps; step++)
            {
                var current = angle + (sweep * step / steps);

                slice.Points.Add((centerX + (radius * Math.Cos(current)), centerY + (radius * Math.Sin(current))));
            }

            items.Add(slice);

            if (spec.DataLabels && sweep > 0.2)
            {
                var middle = angle + (sweep / 2);
                var labelRadius = spec.Kind == "doughnut" ? radius * 0.78 : radius * 0.62;
                var percent = (values[index] / total).ToString("0%", CultureInfo.InvariantCulture);

                items.Add(Text(percent, centerX + (labelRadius * Math.Cos(middle)), centerY + (labelRadius * Math.Sin(middle)) + 3, 8, bold: true, "FFFFFF", source, anchor: "middle"));
            }

            angle += sweep;
        }

        if (spec.Kind == "doughnut")
        {
            var hole = radius * 0.55;

            items.Add(new WordRectItem { X = centerX - hole, Y = centerY - hole, Width = hole * 2, Height = hole * 2, Fill = "FFFFFF", Ellipse = true, Source = source });
        }
    }

    private static void DrawAxes(WordChartSpec spec, List<WordChartSeries> series, List<WordDrawItem> items, double left, double top, double right, double bottom, OpenXmlElement source)
    {
        var stacked = spec.Grouping is "stacked" or "percentStacked";
        var percent = spec.Grouping == "percentStacked";
        var horizontal = spec.IsHorizontal;
        var scatter = spec.Kind == "scatter";
        var count = scatter ? series.Max(item => item.Values.Count) : Math.Max(spec.Labels.Count, series.Max(item => item.Values.Count));
        double minimum, maximum;

        if (stacked)
        {
            var positive = Enumerable.Range(0, count).Select(index => series.Sum(item => Math.Max(0, Value(item, index)))).DefaultIfEmpty(0).Max();
            var negative = Enumerable.Range(0, count).Select(index => series.Sum(item => Math.Min(0, Value(item, index)))).DefaultIfEmpty(0).Min();

            minimum = percent ? 0 : negative;
            maximum = percent ? 1 : positive;
        }
        else
        {
            var values = series.SelectMany(item => item.Values).Where(value => value is not null).Select(value => Clean(value.Value)).DefaultIfEmpty(0).ToList();

            minimum = Math.Min(0, values.Min());
            maximum = Math.Max(0, values.Max());
        }

        var (low, high, step) = NiceScale(minimum, maximum);
        var format = percent ? "0%" : spec.NumberFormat;
        var labelWidth = Math.Max(Measure(FormatValue(high, format), 8), Measure(FormatValue(low, format), 8)) + 6;
        var plotLeft = horizontal ? left + Math.Min((right - left) * 0.3, spec.Labels.Select(label => Measure(label ?? string.Empty, 8)).DefaultIfEmpty(20).Max() + 8) : left + labelWidth + (string.IsNullOrWhiteSpace(spec.YAxisTitle) ? 0 : 14);
        var plotRight = right - 4;
        var plotTop = top + 4;
        var plotBottom = bottom - (horizontal ? 14 : 16) - (string.IsNullOrWhiteSpace(spec.XAxisTitle) ? 0 : 14);
        var plotWidth = Math.Max(10, plotRight - plotLeft);
        var plotHeight = Math.Max(10, plotBottom - plotTop);

        double ValuePosition(double value) => horizontal
            ? plotLeft + ((value - low) / (high - low) * plotWidth)
            : plotBottom - ((value - low) / (high - low) * plotHeight);

        // The ticks are counted rather than stepped to the top, so a step too small to move a huge value still
        // ends the loop.
        for (var index = 0; index <= MaxTicks; index++)
        {
            var tick = low + (index * step);

            if (tick > high + (step / 2))
            {
                break;
            }

            var position = ValuePosition(tick);

            if (horizontal)
            {
                items.Add(new WordLineItem { X1 = position, Y1 = plotTop, X2 = position, Y2 = plotBottom, Color = GridColor, Width = 0.5, Source = source });
                items.Add(Text(FormatValue(tick, format), position, plotBottom + 11, 8, bold: false, AxisColor, source, anchor: "middle"));
            }
            else
            {
                items.Add(new WordLineItem { X1 = plotLeft, Y1 = position, X2 = plotRight, Y2 = position, Color = GridColor, Width = 0.5, Source = source });
                items.Add(Text(FormatValue(tick, format), plotLeft - 4, position + 3, 8, bold: false, AxisColor, source, anchor: "end"));
            }
        }

        var zero = ValuePosition(Math.Clamp(0, low, high));

        items.Add(horizontal
            ? new WordLineItem { X1 = zero, Y1 = plotTop, X2 = zero, Y2 = plotBottom, Color = "BFBFBF", Width = 0.75, Source = source }
            : new WordLineItem { X1 = plotLeft, Y1 = zero, X2 = plotRight, Y2 = zero, Color = "BFBFBF", Width = 0.75, Source = source });

        if (!string.IsNullOrWhiteSpace(spec.XAxisTitle))
        {
            items.Add(Text(spec.XAxisTitle, (plotLeft + plotRight) / 2, bottom - 2, 8, bold: true, AxisColor, source, anchor: "middle"));
        }

        if (!string.IsNullOrWhiteSpace(spec.YAxisTitle))
        {
            items.Add(Text(spec.YAxisTitle, left + 2, plotTop - 2, 8, bold: true, AxisColor, source));
        }

        if (scatter)
        {
            DrawScatter(spec, series, items, plotLeft, plotWidth, ValuePosition, source);

            return;
        }

        var slot = (horizontal ? plotHeight : plotWidth) / Math.Max(1, count);
        var everyLabel = Math.Max(1, (int)Math.Ceiling(count / Math.Max(1, (horizontal ? plotHeight : plotWidth) / 40)));

        for (var index = 0; index < count; index++)
        {
            if (index % everyLabel != 0)
            {
                continue;
            }

            var label = index < spec.Labels.Count ? spec.Labels[index] ?? string.Empty : string.Empty;
            var center = (horizontal ? plotTop : plotLeft) + (slot * (index + 0.5));

            items.Add(horizontal
                ? Text(Clip(label, plotLeft - left - 6, 8), plotLeft - 4, center + 3, 8, bold: false, AxisColor, source, anchor: "end")
                : Text(Clip(label, slot * everyLabel - 2, 8), center, plotBottom + 12, 8, bold: false, AxisColor, source, anchor: "middle"));
        }

        switch (spec.Kind)
        {
            case "line":
            case "area":
                DrawLines(spec, series, items, count, slot, horizontal ? plotTop : plotLeft, ValuePosition, zero, stacked, percent, source);

                break;

            default:
                DrawBars(spec, series, items, count, slot, horizontal, horizontal ? plotTop : plotLeft, ValuePosition, stacked, percent, source);

                break;
        }
    }

    private static void DrawBars(WordChartSpec spec, List<WordChartSeries> series, List<WordDrawItem> items, int count, double slot, bool horizontal, double start, Func<double, double> position, bool stacked, bool percent, OpenXmlElement source)
    {
        var groupWidth = slot * 0.7;
        var barWidth = stacked ? groupWidth : groupWidth / series.Count;

        for (var index = 0; index < count; index++)
        {
            var total = percent ? series.Sum(item => Math.Abs(Value(item, index))) : 1;
            double positive = 0, negative = 0;

            for (var seriesIndex = 0; seriesIndex < series.Count; seriesIndex++)
            {
                var raw = Value(series[seriesIndex], index);
                var value = percent && total > 0 ? raw / total : raw;
                double from, to;

                if (stacked)
                {
                    from = value >= 0 ? positive : negative;
                    to = from + value;

                    if (value >= 0)
                    {
                        positive = to;
                    }
                    else
                    {
                        negative = to;
                    }
                }
                else
                {
                    from = 0;
                    to = value;
                }

                var offset = start + (slot * index) + ((slot - groupWidth) / 2) + (stacked ? 0 : barWidth * seriesIndex);
                var a = position(from);
                var b = position(to);
                var color = WordChartWriter.SeriesColor(spec, spec.Series.IndexOf(series[seriesIndex]));

                items.Add(horizontal
                    ? new WordRectItem { X = Math.Min(a, b), Y = offset, Width = Math.Abs(b - a), Height = barWidth * 0.95, Fill = color, Source = source }
                    : new WordRectItem { X = offset, Y = Math.Min(a, b), Width = barWidth * 0.95, Height = Math.Abs(b - a), Fill = color, Source = source });

                if (spec.DataLabels && Math.Abs(b - a) > 8)
                {
                    var label = FormatValue(raw, spec.NumberFormat);

                    items.Add(horizontal
                        ? Text(label, Math.Max(a, b) + 2, offset + (barWidth / 2) + 3, 7, bold: false, "404040", source)
                        : Text(label, offset + (barWidth / 2), Math.Min(a, b) - 2, 7, bold: false, "404040", source, anchor: "middle"));
                }
            }
        }
    }

    private static void DrawLines(WordChartSpec spec, List<WordChartSeries> series, List<WordDrawItem> items, int count, double slot, double start, Func<double, double> position, double zero, bool stacked, bool percent, OpenXmlElement source)
    {
        var running = new double[count];

        for (var seriesIndex = 0; seriesIndex < series.Count; seriesIndex++)
        {
            var color = WordChartWriter.SeriesColor(spec, spec.Series.IndexOf(series[seriesIndex]));
            var path = new WordPolygonItem { Stroke = color, StrokeWidth = 2, Open = spec.Kind == "line", Source = source };
            var baseline = new List<(double X, double Y)>();

            for (var index = 0; index < count; index++)
            {
                var raw = Value(series[seriesIndex], index);
                var total = percent ? series.Sum(item => Math.Abs(Value(item, index))) : 1;
                var value = percent && total > 0 ? raw / total : raw;
                var x = start + (slot * (index + 0.5));

                baseline.Add((x, stacked ? position(running[index]) : zero));

                if (stacked)
                {
                    running[index] += value;
                    value = running[index];
                }

                var y = position(value);

                path.Points.Add((x, y));

                if (spec.Kind == "line")
                {
                    items.Add(new WordRectItem { X = x - 2.5, Y = y - 2.5, Width = 5, Height = 5, Fill = color, Ellipse = true, Source = source });
                }

                if (spec.DataLabels)
                {
                    items.Add(Text(FormatValue(raw, spec.NumberFormat), x, y - 5, 7, bold: false, "404040", source, anchor: "middle"));
                }
            }

            if (spec.Kind == "area")
            {
                baseline.Reverse();
                path.Points.AddRange(baseline);
                path.Fill = color;
                path.Stroke = null;
                items.Insert(Math.Min(items.Count, 1), path);
            }
            else
            {
                items.Add(path);
            }
        }
    }

    private static void DrawScatter(WordChartSpec spec, List<WordChartSeries> series, List<WordDrawItem> items, double plotLeft, double plotWidth, Func<double, double> position, OpenXmlElement source)
    {
        var xs = series.SelectMany(item => item.XValues.Count > 0 ? item.XValues : [.. Enumerable.Range(1, item.Values.Count).Select(number => (double?)number)]).Where(value => value is not null).Select(value => Clean(value.Value)).ToList();
        var (low, high, _) = NiceScale(xs.DefaultIfEmpty(0).Min(), xs.DefaultIfEmpty(1).Max());

        foreach (var item in series)
        {
            var color = WordChartWriter.SeriesColor(spec, spec.Series.IndexOf(item));

            for (var index = 0; index < item.Values.Count; index++)
            {
                if (item.Values[index] is not { } y)
                {
                    continue;
                }

                var xValue = index < item.XValues.Count && item.XValues[index] is { } given ? Clean(given) : index + 1;
                var x = plotLeft + ((xValue - low) / (high - low) * plotWidth);

                items.Add(new WordRectItem { X = x - 3, Y = position(Clean(y)) - 3, Width = 6, Height = 6, Fill = color, Ellipse = true, Source = source });
            }
        }
    }

    private static double Value(WordChartSeries series, int index)
    {
        return index < series.Values.Count && series.Values[index] is { } value ? Clean(value) : 0;
    }

    // A chart's cached values come from the document: values that are not numbers count as zero, and values past
    // any real chart's scale are held to it, so the axis arithmetic stays finite.
    private static double Clean(double value)
    {
        return double.IsFinite(value) ? Math.Clamp(value, -MaxValue, MaxValue) : 0;
    }

    /// <summary>
    /// Works out axis bounds and a step that land on round numbers.
    /// </summary>
    /// <param name="minimum">The smallest value.</param>
    /// <param name="maximum">The largest value.</param>
    /// <returns>The axis low and high ends and the step between gridlines.</returns>
    public static (double Low, double High, double Step) NiceScale(double minimum, double maximum)
    {
        minimum = Clean(minimum);
        maximum = Clean(maximum);

        if (maximum <= minimum)
        {
            maximum = minimum + 1;
        }

        var range = maximum - minimum;
        var rough = range / 5;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var residual = rough / magnitude;
        var step = (residual <= 1 ? 1 : residual <= 2 ? 2 : residual <= 2.5 ? 2.5 : residual <= 5 ? 5 : 10) * magnitude;

        // A value so large that adding one does not change it leaves no step to take.
        if (!double.IsFinite(step) || step <= 0 || minimum + step == minimum)
        {
            return (minimum, minimum + 1, 0.2);
        }

        var low = Math.Floor(minimum / step) * step;
        var high = Math.Ceiling(maximum / step) * step;

        if (high <= low)
        {
            high = low + step;
        }

        return (low, high, step);
    }

    private static string FormatValue(double value, string format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return Math.Abs(value) >= 1000 ? value.ToString("#,##0.##", CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        return TabularPreviewValueFormat.Format(value, new SpreadsheetColumnFormat { FormatCode = format });
    }

    private static WordTextItem Text(string text, double x, double baseline, double size, bool bold, string color, OpenXmlElement source, string anchor = "start", double maxWidth = 0)
    {
        text = Shorten(text);

        var format = new WordResolvedRun { Font = "Calibri", Size = size, Bold = bold, Color = color };
        var width = WordTextMeasurer.Measure(text, format.Font, size, bold);

        if (maxWidth > 0 && width > maxWidth - 8)
        {
            text = Clip(text, maxWidth - 8, size);
            width = WordTextMeasurer.Measure(text, format.Font, size, bold);
        }

        var left = anchor switch
        {
            "middle" => x - (width / 2),
            "end" => x - width,
            _ => x,
        };

        return new WordTextItem { X = left, Baseline = baseline, Width = width, Text = text, Format = format, Source = source };
    }

    private static double Measure(string text, double size)
    {
        return WordTextMeasurer.Measure(text ?? string.Empty, "Calibri", size, bold: false);
    }

    private static string Shorten(string text)
    {
        text ??= string.Empty;

        return text.Length > MaxLabelLength ? text[..(MaxLabelLength - 1)] + "…" : text;
    }

    private static string Clip(string text, double width, double size)
    {
        text = Shorten(text);

        if (Measure(text, size) <= width || text.Length <= 3)
        {
            return text;
        }

        // The longest start of the text that fits with an ellipsis, found by halving rather than one character
        // at a time.
        var low = 1;
        var high = text.Length - 1;

        while (low < high)
        {
            var middle = (low + high + 1) / 2;

            if (Measure(string.Concat(text.AsSpan(0, middle), "…"), size) <= width)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return text[..low] + "…";
    }
}
