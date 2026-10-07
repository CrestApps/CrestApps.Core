using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace CrestApps.Core.AI.Documents.OpenXml.Presentations;

/// <summary>
/// Reads a chart part from the values it caches, so a chart can be described and drawn without its workbook.
/// </summary>
internal static class OpenXmlChartReader
{
    /// <summary>
    /// Reads a chart.
    /// </summary>
    /// <param name="part">The chart part.</param>
    /// <param name="context">The slide context, for theme colours.</param>
    /// <returns>The chart.</returns>
    public static PresentationChart Read(ChartPart part, OpenXmlSlideContext context)
    {
        var chart = new PresentationChart();
        var root = part?.ChartSpace;
        var chartElement = OpenXmlMarkup.Child(root, "chart");

        if (chartElement is null)
        {
            chart.Kind = "other";

            return chart;
        }

        chart.HasEmbeddedWorkbook = part.EmbeddedPackagePart is not null || OpenXmlMarkup.Child(root, "externalData") is not null;

        var titleDeleted = OpenXmlMarkup.Bool(OpenXmlMarkup.Child(chartElement, "autoTitleDeleted"), "val") == true;
        var title = OpenXmlMarkup.Child(chartElement, "title");

        if (title is not null && !titleDeleted)
        {
            chart.Title = ReadText(title);
        }

        var plotArea = OpenXmlMarkup.Child(chartElement, "plotArea");
        var plot = plotArea?.ChildElements.FirstOrDefault(element => element.LocalName.EndsWith("Chart", StringComparison.Ordinal));

        if (plot is null)
        {
            chart.Kind = "other";

            return chart;
        }

        chart.Kind = plot.LocalName switch
        {
            "barChart" or "bar3DChart" => OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(plot, "barDir"), "val") == "bar" ? "bar" : "column",
            "lineChart" or "line3DChart" or "stockChart" => "line",
            "pieChart" or "pie3DChart" or "ofPieChart" => "pie",
            "doughnutChart" => "doughnut",
            "areaChart" or "area3DChart" => "area",
            "scatterChart" => "scatter",
            "radarChart" => "radar",
            "bubbleChart" => "bubble",
            _ => "other",
        };

        var grouping = OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(plot, "grouping"), "val");
        chart.Stacked = grouping is "stacked" or "percentStacked";
        chart.PercentStacked = grouping == "percentStacked";

        var seriesIndex = 0;

        foreach (var series in OpenXmlMarkup.Children(plot, "ser"))
        {
            chart.Series.Add(ReadSeries(series, chart, context, seriesIndex++));
        }

        var legend = OpenXmlMarkup.Child(chartElement, "legend");
        chart.ShowLegend = legend is not null;
        chart.LegendPosition = OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(legend, "legendPos"), "val") switch
        {
            "t" => "top",
            "l" => "left",
            "r" or "tr" => "right",
            _ => "bottom",
        };

        chart.ShowDataLabels =
            OpenXmlMarkup.Bool(OpenXmlMarkup.Path(plot, "dLbls", "showVal"), "val") == true ||
            OpenXmlMarkup.Children(plot, "ser").Any(series => OpenXmlMarkup.Bool(OpenXmlMarkup.Path(series, "dLbls", "showVal"), "val") == true);

        var valueAxis = OpenXmlMarkup.Child(plotArea, "valAx");
        var categoryAxis = OpenXmlMarkup.Child(plotArea, "catAx") ?? OpenXmlMarkup.Child(plotArea, "dateAx");

        chart.ValueAxisTitle = ReadText(OpenXmlMarkup.Child(valueAxis, "title"));
        chart.CategoryAxisTitle = ReadText(OpenXmlMarkup.Child(categoryAxis, "title"));
        chart.NumberFormat ??= OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(valueAxis, "numFmt"), "formatCode");

        var textProperties = OpenXmlMarkup.Descendant(OpenXmlMarkup.Child(root, "txPr"), "defRPr");

        if (textProperties is not null)
        {
            chart.Font = context.Theme.ResolveFont(OpenXmlMarkup.Attribute(OpenXmlMarkup.Child(textProperties, "latin"), "typeface"));
            chart.TextColor = context.Colors.ResolveChild(OpenXmlMarkup.Child(textProperties, "solidFill"))?.Hex;

            if (OpenXmlMarkup.Long(textProperties, "sz") is { } size)
            {
                chart.FontSize = size / 100d;
            }
        }

        return chart;
    }

    private static PresentationChartSeries ReadSeries(OpenXmlElement series, PresentationChart chart, OpenXmlSlideContext context, int index)
    {
        var result = new PresentationChartSeries
        {
            Name = ReadSeriesName(OpenXmlMarkup.Child(series, "tx")),
        };

        var categories = OpenXmlMarkup.Child(series, "cat") ?? OpenXmlMarkup.Child(series, "xVal");
        var values = OpenXmlMarkup.Child(series, "val") ?? OpenXmlMarkup.Child(series, "yVal");

        if (chart.Kind == "scatter")
        {
            result.XValues = ReadNumbers(OpenXmlMarkup.Child(series, "xVal"), out _);
        }

        if (chart.Categories.Count == 0 && categories is not null)
        {
            foreach (var category in ReadStrings(categories))
            {
                chart.Categories.Add(category);
            }
        }

        result.Values = ReadNumbers(values, out var format);
        chart.NumberFormat ??= format;

        var properties = OpenXmlMarkup.Child(series, "spPr");
        var fill = OpenXmlMarkup.Child(properties, "solidFill") ?? OpenXmlMarkup.Path(properties, "ln", "solidFill");
        result.Color = context.Colors.ResolveChild(fill)?.Hex ?? DefaultColor(context, index);

        foreach (var point in OpenXmlMarkup.Children(series, "dPt"))
        {
            var pointIndex = (int)(OpenXmlMarkup.Long(OpenXmlMarkup.Child(point, "idx"), "val") ?? -1);
            var pointColor = context.Colors.ResolveChild(OpenXmlMarkup.Path(point, "spPr", "solidFill"))?.Hex;

            if (pointIndex < 0 || pointColor is null)
            {
                continue;
            }

            while (result.PointColors.Count <= pointIndex)
            {
                result.PointColors.Add(null);
            }

            result.PointColors[pointIndex] = pointColor;
        }

        if (chart.Kind is "pie" or "doughnut")
        {
            var count = Math.Max(result.Values.Count, chart.Categories.Count);

            for (var point = 0; point < count; point++)
            {
                if (point >= result.PointColors.Count)
                {
                    result.PointColors.Add(null);
                }

                result.PointColors[point] ??= DefaultColor(context, point);
            }
        }

        return result;
    }

    private static string DefaultColor(OpenXmlSlideContext context, int index)
    {
        var slot = "accent" + ((index % 6) + 1).ToString(CultureInfo.InvariantCulture);
        var color = context.Colors.ResolveScheme(slot) ?? "4472C4";

        // Past the sixth series PowerPoint repeats the accents darker, then lighter.
        return (index / 6) switch
        {
            0 => color,
            1 => PresentationColor.ModulateLuminance(color, 0.6, 0),
            _ => PresentationColor.ModulateLuminance(color, 0.6, 0.4),
        };
    }

    private static string ReadSeriesName(OpenXmlElement text)
    {
        if (text is null)
        {
            return null;
        }

        var direct = OpenXmlMarkup.Child(text, "v");

        if (direct is not null)
        {
            return direct.InnerText;
        }

        var cached = ReadStrings(text);

        return cached.Count > 0 ? string.Join(' ', cached) : null;
    }

    private static List<string> ReadStrings(OpenXmlElement container)
    {
        // Categories may be cached as strings, as numbers, as literals, or as several levels of labels; the
        // innermost level is the one drawn along the axis.
        var cache =
            OpenXmlMarkup.Path(container, "strRef", "strCache") ??
            OpenXmlMarkup.Path(container, "numRef", "numCache") ??
            OpenXmlMarkup.Child(container, "strLit") ??
            OpenXmlMarkup.Child(container, "numLit") ??
            OpenXmlMarkup.Path(container, "multiLvlStrRef", "multiLvlStrCache", "lvl");

        var count = (int)Math.Clamp(OpenXmlMarkup.Long(OpenXmlMarkup.Child(cache, "ptCount"), "val") ?? 0, 0, 10_000);
        var values = new List<string>(count);

        foreach (var point in OpenXmlMarkup.Children(cache, "pt"))
        {
            var pointIndex = (int)Math.Clamp(OpenXmlMarkup.Long(point, "idx") ?? values.Count, 0, 10_000);

            while (values.Count < pointIndex)
            {
                values.Add(string.Empty);
            }

            var value = OpenXmlMarkup.Child(point, "v")?.InnerText ?? string.Empty;

            if (pointIndex < values.Count)
            {
                values[pointIndex] = value;
            }
            else
            {
                values.Add(value);
            }
        }

        while (values.Count < count)
        {
            values.Add(string.Empty);
        }

        return values;
    }

    private static List<double?> ReadNumbers(OpenXmlElement container, out string format)
    {
        var cache =
            OpenXmlMarkup.Path(container, "numRef", "numCache") ??
            OpenXmlMarkup.Child(container, "numLit");

        format = OpenXmlMarkup.Child(cache, "formatCode")?.InnerText;

        if (format == "General")
        {
            format = null;
        }

        var count = (int)Math.Clamp(OpenXmlMarkup.Long(OpenXmlMarkup.Child(cache, "ptCount"), "val") ?? 0, 0, 10_000);
        var values = new List<double?>(count);

        for (var index = 0; index < count; index++)
        {
            values.Add(null);
        }

        foreach (var point in OpenXmlMarkup.Children(cache, "pt"))
        {
            var pointIndex = (int)Math.Clamp(OpenXmlMarkup.Long(point, "idx") ?? values.Count, 0, 10_000);

            while (values.Count <= pointIndex)
            {
                values.Add(null);
            }

            if (double.TryParse(OpenXmlMarkup.Child(point, "v")?.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                values[pointIndex] = number;
            }
        }

        return values;
    }

    private static string ReadText(OpenXmlElement title)
    {
        if (title is null)
        {
            return null;
        }

        var rich = OpenXmlMarkup.Path(title, "tx", "rich");

        if (rich is not null)
        {
            var builder = new StringBuilder();

            foreach (var paragraph in OpenXmlMarkup.Children(rich, "p"))
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                foreach (var run in paragraph.ChildElements)
                {
                    if (run.LocalName is "r" or "fld")
                    {
                        builder.Append(OpenXmlMarkup.Child(run, "t")?.InnerText);
                    }
                }
            }

            return builder.ToString().Trim();
        }

        var reference = OpenXmlMarkup.Child(title, "tx");

        return reference is null ? null : ReadSeriesName(reference);
    }
}
