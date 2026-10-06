using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Shapes.Charts;
using MigraDoc.DocumentObjectModel.Tables;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

internal sealed partial class PdfMigraDocBuilder
{
    /// <summary>
    /// Reads a chart type, accepting the names a model and Chart.js use.
    /// </summary>
    /// <remarks>
    /// A "bar chart" is drawn with upright bars, as Chart.js and most people mean it; only a chart asked for
    /// as horizontal lays its bars sideways.
    /// </remarks>
    /// <param name="value">The chart type as written.</param>
    /// <returns>The MigraDoc chart type.</returns>
    public static ChartType ReadChartType(string value)
    {
        return value?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
        {
            "horizontal_bar" or "hbar" or "bar_horizontal" or "horizontal" => ChartType.Bar2D,
            "stacked_horizontal_bar" or "horizontal_stacked_bar" or "stacked_hbar" => ChartType.BarStacked2D,
            "stacked_bar" or "stacked_column" or "stacked" or "stacked_vertical" => ChartType.ColumnStacked2D,
            "line" or "trend" or "radar" => ChartType.Line,
            "area" => ChartType.Area2D,
            "pie" or "doughnut" or "donut" or "polararea" or "polar_area" => ChartType.Pie2D,
            "exploded_pie" => ChartType.PieExploded2D,
            _ => ChartType.Column2D,
        };
    }

    private void AddChart(Section section, PdfChartDefinition definition)
    {
        var series = (definition.Series ?? [])
            .Where(candidate => candidate?.Values is { Count: > 0 })
            .ToList();

        if (series.Count == 0)
        {
            _warnings.Add($"The chart \"{definition.Title}\" has no values to plot and was left out.");

            return;
        }

        var labels = definition.Labels ?? [];
        var categoryCount = Math.Max(labels.Count, series.Max(candidate => candidate.Values.Count));

        if (categoryCount > _options.MaxChartCategories)
        {
            _warnings.Add($"The chart \"{definition.Title}\" had {categoryCount} categories; the first {_options.MaxChartCategories} were plotted. Plot a top-N or grouped result instead.");
            categoryCount = _options.MaxChartCategories;
        }

        var type = ReadChartType(definition.ChartType);
        var isPie = type is ChartType.Pie2D or ChartType.PieExploded2D;
        var isHorizontal = type is ChartType.Bar2D or ChartType.BarStacked2D;
        var numberFormat = string.IsNullOrWhiteSpace(definition.NumberFormat) ? DefaultNumberFormat(series) : definition.NumberFormat;

        if (isPie && series.Count > 1)
        {
            _warnings.Add($"A pie chart shows one series; \"{series[0].Name ?? "the first series"}\" was plotted and the others were left out.");
            series = [series[0]];
        }

        var width = _page.UsableWidth * Math.Clamp(definition.WidthPercent ?? 100, 20, 100) / 100;
        var height = Math.Clamp(definition.Height ?? DefaultHeight(definition, isPie, isHorizontal, categoryCount, series.Count), 80, _page.UsableHeight * 0.9);
        var chart = new Chart(type)
        {
            Width = Unit.FromPoint(width),
            Height = Unit.FromPoint(height),
            Left = ShapePosition.Center,
        };

        chart.Format.Font.Name = _theme.FontFamily;
        chart.Format.Font.Size = Math.Max(6, _theme.BaseFontSize - 2.5);
        chart.Format.Font.Color = _theme.Text.ToMigraDoc();

        if (!string.IsNullOrWhiteSpace(definition.Title))
        {
            var title = chart.HeaderArea.AddParagraph(definition.Title);
            title.Format.Font.Bold = true;
            title.Format.Font.Size = Math.Max(7, _theme.BaseFontSize - 0.5);
            title.Format.Font.Color = _theme.Heading.ToMigraDoc();
            title.Format.Alignment = ParagraphAlignment.Center;
        }

        for (var seriesIndex = 0; seriesIndex < series.Count; seriesIndex++)
        {
            var source = series[seriesIndex];
            var plotted = chart.SeriesCollection.AddSeries();
            var color = PdfResolvedTheme.ReadColor(source.Color, _theme.ChartColor(seriesIndex), "series color", _warnings);

            plotted.Name = string.IsNullOrWhiteSpace(source.Name) ? $"Series {seriesIndex + 1}" : source.Name;

            for (var index = 0; index < categoryCount; index++)
            {
                var value = index < source.Values.Count ? source.Values[index] : null;

                if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                {
                    plotted.AddBlank();

                    continue;
                }

                var point = plotted.Add(value.Value);

                if (isPie)
                {
                    point.FillFormat.Color = _theme.ChartColor(index).ToMigraDoc();
                    point.LineFormat.Color = _white.ToMigraDoc();
                    point.LineFormat.Width = 0.75;
                }
            }

            if (type == ChartType.Line)
            {
                plotted.LineFormat.Color = color.ToMigraDoc();
                plotted.LineFormat.Width = 2;
                plotted.MarkerStyle = MarkerStyle.Circle;
                plotted.MarkerSize = Unit.FromPoint(4);
                plotted.MarkerBackgroundColor = color.ToMigraDoc();
                plotted.MarkerForegroundColor = color.ToMigraDoc();
            }
            else if (!isPie)
            {
                plotted.FillFormat.Color = color.ToMigraDoc();
                plotted.LineFormat.Visible = false;
            }
        }

        var categories = chart.XValues.AddXSeries();

        for (var index = 0; index < categoryCount; index++)
        {
            categories.Add(index < labels.Count ? labels[index] ?? string.Empty : string.Empty);
        }

        if (!isPie)
        {
            chart.XAxis.MajorTickMark = TickMarkType.Outside;
            chart.XAxis.LineFormat.Color = _theme.Muted.Blend(_white, 0.4).ToMigraDoc();
            chart.YAxis.MajorTickMark = TickMarkType.Outside;
            chart.YAxis.HasMajorGridlines = true;
            chart.YAxis.MajorGridlines.LineFormat.Color = _theme.Muted.Blend(_white, 0.8).ToMigraDoc();
            chart.YAxis.MajorGridlines.LineFormat.Width = 0.5;
            chart.YAxis.LineFormat.Visible = false;

            // Values read with thousands separators: "150,000", not "150000.0".
            chart.YAxis.TickLabels.Format = numberFormat;

            // MigraDoc's value axis is the Y axis whichever way the bars run; its title is turned upright only
            // when that axis is upright, or it is written across the axis labels.
            if (!string.IsNullOrWhiteSpace(definition.XAxisTitle))
            {
                chart.XAxis.Title.Caption = definition.XAxisTitle;

                if (isHorizontal)
                {
                    chart.XAxis.Title.Orientation = 90;
                    chart.XAxis.Title.VerticalAlignment = VerticalAlignment.Center;
                }
            }

            if (!string.IsNullOrWhiteSpace(definition.YAxisTitle))
            {
                chart.YAxis.Title.Caption = definition.YAxisTitle;

                if (!isHorizontal)
                {
                    chart.YAxis.Title.Orientation = 90;
                    chart.YAxis.Title.VerticalAlignment = VerticalAlignment.Center;
                }
            }

            chart.PlotArea.LineFormat.Visible = false;
        }

        if (definition.Legend ?? (isPie || series.Count > 1))
        {
            var legend = isPie
                ? chart.RightArea.AddLegend()
                : chart.BottomArea.AddLegend();

            legend.Format.Font.Size = Math.Max(6, _theme.BaseFontSize - 2.5);
        }

        if (definition.DataLabels ?? isPie)
        {
            chart.DataLabel.Type = isPie ? DataLabelType.Percent : DataLabelType.Value;
            chart.DataLabel.Position = isPie ? DataLabelPosition.OutsideEnd : DataLabelPosition.OutsideEnd;
            chart.DataLabel.Font.Size = Math.Max(6, _theme.BaseFontSize - 3);

            if (!isPie)
            {
                chart.DataLabel.Format = numberFormat;
            }
        }

        var holder = section.AddParagraph();
        holder.Format.Alignment = ParagraphAlignment.Center;
        holder.Format.SpaceBefore = Unit.FromPoint(6);
        holder.Format.SpaceAfter = Unit.FromPoint(string.IsNullOrWhiteSpace(definition.Caption) ? 10 : 2);
        holder.Format.KeepWithNext = !string.IsNullOrWhiteSpace(definition.Caption);

        section.Add(chart);

        AddCaption(section, definition.Caption);
    }

    private static string DefaultNumberFormat(List<PdfChartSeriesDefinition> series)
    {
        var values = series
            .SelectMany(candidate => candidate.Values)
            .Where(value => value is { } number && double.IsFinite(number))
            .Select(value => value.Value)
            .ToList();

        // Whole numbers are shown whole; fractions keep up to two places, so 0.25 is not shown as 0.
        return values.Count == 0 || values.All(value => Math.Abs(value - Math.Round(value)) < 1e-9)
            ? "#,##0"
            : "#,##0.##";
    }

    private static double DefaultHeight(PdfChartDefinition definition, bool isPie, bool isHorizontal, int categories, int seriesCount)
    {
        if (isPie)
        {
            return 260;
        }

        // The plot gets what the title, the legend and the axis titles leave, so each of them adds room
        // rather than squeezing the bars.
        var hasLegend = definition.Legend ?? seriesCount > 1;
        var chrome = (string.IsNullOrWhiteSpace(definition.Title) ? 0 : 22) +
            (hasLegend ? 24 : 0) +
            (string.IsNullOrWhiteSpace(isHorizontal ? definition.YAxisTitle : definition.XAxisTitle) ? 0 : 16);

        // Sideways bars need height for every bar; upright ones need a plot tall enough to compare them.
        var plot = isHorizontal
            ? Math.Max(150, categories * ((seriesCount * 14) + 12))
            : 210;

        return plot + chrome + 30;
    }
}
