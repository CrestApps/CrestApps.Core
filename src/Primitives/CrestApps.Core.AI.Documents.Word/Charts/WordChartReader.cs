using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace CrestApps.Core.AI.Documents.Word.Charts;

/// <summary>
/// Reads a chart back from its part — its type, title, categories, series and colors — from the data the chart
/// caches, so it can be drawn in a preview and changed without the workbook it came from.
/// </summary>
internal static class WordChartReader
{
    private static readonly XNamespace _c = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace _a = "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>
    /// Reads a chart.
    /// </summary>
    /// <param name="chartPart">The chart part.</param>
    /// <returns>The chart, or <see langword="null"/> when the part cannot be read.</returns>
    public static WordChartSpec Read(ChartPart chartPart)
    {
        if (chartPart is null)
        {
            return null;
        }

        XDocument document;

        try
        {
            using var stream = chartPart.GetStream(FileMode.Open, FileAccess.Read);

            document = XDocument.Load(stream);
        }
        catch (Exception ex) when (ex is XmlException or IOException or InvalidOperationException)
        {
            return null;
        }

        var chart = document.Root?.Element(_c + "chart");
        var plotArea = chart?.Element(_c + "plotArea");

        if (plotArea is null)
        {
            return null;
        }

        var plot = plotArea.Elements().FirstOrDefault(element => element.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal));

        if (plot is null)
        {
            return null;
        }

        var spec = new WordChartSpec
        {
            Type = ReadType(plot),
            Title = ReadText(chart.Element(_c + "title")),
            Legend = ReadLegend(chart.Element(_c + "legend")),
            DataLabels = plot.Element(_c + "dLbls")?.Element(_c + "showVal")?.Attribute("val")?.Value == "1" ||
                plot.Element(_c + "dLbls")?.Element(_c + "showPercent")?.Attribute("val")?.Value == "1",
            XAxisTitle = ReadText(plotArea.Element(_c + "catAx")?.Element(_c + "title") ?? plotArea.Elements(_c + "valAx").FirstOrDefault()?.Element(_c + "title")),
            YAxisTitle = ReadText(plotArea.Elements(_c + "valAx").LastOrDefault()?.Element(_c + "title")),
            NumberFormat = plotArea.Elements(_c + "valAx").LastOrDefault()?.Element(_c + "numFmt")?.Attribute("formatCode")?.Value,
            Smooth = plot.Descendants(_c + "smooth").Any(element => element.Attribute("val")?.Value is "1" or "true"),
        };

        if (string.Equals(spec.NumberFormat, "General", StringComparison.OrdinalIgnoreCase))
        {
            spec.NumberFormat = null;
        }

        var scatter = spec.Kind == "scatter";

        foreach (var series in plot.Elements(_c + "ser"))
        {
            var item = new WordChartSeries
            {
                Name = ReadStrings(series.Element(_c + "tx")).FirstOrDefault() ?? series.Element(_c + "tx")?.Element(_c + "v")?.Value,
                Values = ReadNumbers(series.Element(scatter ? _c + "yVal" : _c + "val")),
                Color = series.Element(_c + "spPr")?.Descendants(_a + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value,
            };

            if (scatter)
            {
                item.XValues = ReadNumbers(series.Element(_c + "xVal"));
            }
            else if (spec.Labels.Count == 0)
            {
                spec.Labels = ReadStrings(series.Element(_c + "cat"));

                if (spec.Labels.Count == 0)
                {
                    spec.Labels = [.. ReadNumbers(series.Element(_c + "cat")).Select(value => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)];
                }
            }

            if (spec.Kind is "pie" or "doughnut")
            {
                foreach (var point in series.Elements(_c + "dPt"))
                {
                    var color = point.Element(_c + "spPr")?.Element(_a + "solidFill")?.Element(_a + "srgbClr")?.Attribute("val")?.Value;

                    if (color is not null)
                    {
                        spec.Colors.Add(color);
                    }
                }
            }

            spec.Series.Add(item);
        }

        return spec;
    }

    private static string ReadType(XElement plot)
    {
        var grouping = plot.Element(_c + "grouping")?.Attribute("val")?.Value;
        var horizontal = plot.Element(_c + "barDir")?.Attribute("val")?.Value == "bar";

        return plot.Name.LocalName switch
        {
            "barChart" or "bar3DChart" => grouping switch
            {
                "stacked" => horizontal ? "stacked_bar" : "stacked_column",
                "percentStacked" => horizontal ? "percent_bar" : "percent_column",
                _ => horizontal ? "bar" : "column",
            },
            "lineChart" or "line3DChart" or "stockChart" or "radarChart" => "line",
            "pieChart" or "pie3DChart" or "ofPieChart" => "pie",
            "doughnutChart" => "doughnut",
            "areaChart" or "area3DChart" => grouping == "stacked" ? "stacked_area" : "area",
            "scatterChart" or "bubbleChart" => "scatter",
            _ => "column",
        };
    }

    private static string ReadLegend(XElement legend)
    {
        if (legend is null)
        {
            return "none";
        }

        return legend.Element(_c + "legendPos")?.Attribute("val")?.Value switch
        {
            "r" => "right",
            "l" => "left",
            "t" => "top",
            _ => "bottom",
        };
    }

    private static string ReadText(XElement title)
    {
        if (title is null)
        {
            return null;
        }

        var text = string.Concat(title.Descendants(_a + "t").Select(element => element.Value));

        if (string.IsNullOrWhiteSpace(text))
        {
            text = string.Join(" ", ReadStrings(title.Element(_c + "tx")));
        }

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static List<string> ReadStrings(XElement container)
    {
        var cache = container?.Descendants().FirstOrDefault(element => element.Name == _c + "strCache" || element.Name == _c + "strLit");

        if (cache is null)
        {
            return [];
        }

        return ReadPoints(cache, value => value);
    }

    private static List<double?> ReadNumbers(XElement container)
    {
        var cache = container?.Descendants().FirstOrDefault(element => element.Name == _c + "numCache" || element.Name == _c + "numLit");

        if (cache is null)
        {
            return [];
        }

        return ReadPoints(cache, value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : (double?)null);
    }

    private static List<T> ReadPoints<T>(XElement cache, Func<string, T> convert)
    {
        var count = int.TryParse(cache.Element(_c + "ptCount")?.Attribute("val")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var declared) ? declared : 0;
        var points = cache.Elements(_c + "pt").ToList();

        count = Math.Max(count, points.Count == 0 ? 0 : points.Max(point => int.TryParse(point.Attribute("idx")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index + 1 : 0));
        count = Math.Min(count, 10_000);

        var values = Enumerable.Repeat(default(T), count).ToList();

        foreach (var point in points)
        {
            if (int.TryParse(point.Attribute("idx")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < count)
            {
                values[index] = convert(point.Element(_c + "v")?.Value);
            }
        }

        return values;
    }
}
