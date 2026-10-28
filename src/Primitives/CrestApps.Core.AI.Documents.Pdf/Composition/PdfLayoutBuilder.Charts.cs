using CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

internal sealed partial class PdfLayoutBuilder
{
    /// <summary>
    /// Reads a chart type, accepting the names a model and Chart.js use.
    /// </summary>
    /// <remarks>
    /// A "bar chart" is drawn with upright bars, as Chart.js and most people mean it; only a chart asked for
    /// as horizontal lays its bars sideways.
    /// </remarks>
    /// <param name="value">The chart type as written.</param>
    /// <returns>The kind of chart.</returns>
    public static PdfChartKind ReadChartType(string value)
    {
        return value?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
        {
            "horizontal_bar" or "hbar" or "bar_horizontal" or "horizontal" => PdfChartKind.Bar,
            "stacked_horizontal_bar" or "horizontal_stacked_bar" or "stacked_hbar" => PdfChartKind.StackedBar,
            "stacked_bar" or "stacked_column" or "stacked" or "stacked_vertical" => PdfChartKind.StackedColumn,
            "line" or "trend" or "radar" => PdfChartKind.Line,
            "area" => PdfChartKind.Area,
            "pie" or "doughnut" or "donut" or "polararea" or "polar_area" => PdfChartKind.Pie,
            "exploded_pie" => PdfChartKind.ExplodedPie,
            _ => PdfChartKind.Column,
        };
    }

    private void AddChart(PdfLayoutSection section, PdfChartDefinition definition)
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

        var kind = ReadChartType(definition.ChartType);
        var isPie = kind is PdfChartKind.Pie or PdfChartKind.ExplodedPie;
        var isHorizontal = kind is PdfChartKind.Bar or PdfChartKind.StackedBar;
        var numberFormat = string.IsNullOrWhiteSpace(definition.NumberFormat) ? DefaultNumberFormat(series) : definition.NumberFormat;

        if (isPie && series.Count > 1)
        {
            _warnings.Add($"A pie chart shows one series; \"{series[0].Name ?? "the first series"}\" was plotted and the others were left out.");
            series = [series[0]];
        }

        var width = _page.UsableWidth * Math.Clamp(definition.WidthPercent ?? 100, 20, 100) / 100;
        var height = Math.Clamp(definition.Height ?? DefaultHeight(definition, isPie, isHorizontal, categoryCount, series.Count), 80, _page.UsableHeight * 0.9);
        var plotted = new List<PdfChartSeriesModel>(series.Count);

        for (var seriesIndex = 0; seriesIndex < series.Count; seriesIndex++)
        {
            var source = series[seriesIndex];
            var color = PdfResolvedTheme.ReadColor(source.Color, _theme.ChartColor(seriesIndex), "series color", _warnings);
            var values = new double?[categoryCount];

            for (var index = 0; index < categoryCount; index++)
            {
                values[index] = index < source.Values.Count && source.Values[index] is { } value && double.IsFinite(value)
                    ? value
                    : null;
            }

            plotted.Add(new PdfChartSeriesModel(string.IsNullOrWhiteSpace(source.Name) ? $"Series {seriesIndex + 1}" : source.Name, values, color));
        }

        var chart = new PdfChartModel
        {
            Kind = kind,
            Title = definition.Title,
            Labels = [.. Enumerable.Range(0, categoryCount).Select(index => index < labels.Count ? labels[index] ?? string.Empty : string.Empty)],
            Series = plotted,
            ShowLegend = definition.Legend ?? (isPie || series.Count > 1),
            ShowDataLabels = definition.DataLabels ?? isPie,
            NumberFormat = numberFormat,
            CategoryAxisTitle = definition.XAxisTitle,
            ValueAxisTitle = definition.YAxisTitle,
            FontFamily = _theme.FontFamily,
            FontSize = Math.Max(6, _theme.BaseFontSize - 2.5),
            TitleSize = Math.Max(7, _theme.BaseFontSize - 0.5),
            TextColor = _theme.Text,
            TitleColor = _theme.Heading,
            GridColor = _theme.Muted.Blend(_white, 0.8),
            AxisColor = _theme.Muted.Blend(_white, 0.4),
            SliceColor = _theme.ChartColor,
        };

        // The chart is spaced from what comes before it by an empty paragraph above it, as it always has been.
        var holder = section.AddParagraph();
        holder.Format.Alignment = PdfLayoutAlignment.Center;
        holder.Format.SpaceBefore = 6;
        holder.Format.SpaceAfter = string.IsNullOrWhiteSpace(definition.Caption) ? 10 : 2;
        holder.Format.KeepWithNext = !string.IsNullOrWhiteSpace(definition.Caption);

        var painter = new PdfChartPainter(chart);

        section.Add(new PdfLayoutGraphic(width, height, (canvas, left, top) => painter.Draw(canvas, left, top, width, height)));

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
