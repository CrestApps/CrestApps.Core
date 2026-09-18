using System.Text.Json;
using System.Text.Json.Serialization;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// Turns a chart read out of a document into the marker the chat surfaces draw.
/// </summary>
/// <remarks>
/// The host already has a contract for drawing a chart: a <c>[chart:{…}]</c> marker carrying a Chart.js
/// config, emitted by the chart tool, read by the shared marker parser and rendered onto a canvas. A chart
/// lifted from a PDF is the same thing arriving from a different direction, so it uses that contract rather
/// than a second one.
/// <para>
/// Without this a reader asking for a chart is shown a picture of one while the model, having been handed
/// the numbers, correctly describes them as machine-readable — the answer and the page disagreeing about
/// what was retrieved.
/// </para>
/// </remarks>
internal static class KnowledgeChartMarker
{
    private static readonly JsonSerializerOptions _options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Builds the marker for a chart, or returns <see langword="null"/> when there is nothing to draw.
    /// </summary>
    /// <param name="chart">The chart detail carried by the knowledge object.</param>
    /// <returns>The <c>[chart:…]</c> marker, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Only a chart whose values were read off the page is drawn. A canvas presents whatever it is given as
    /// measured data — there is no way to plot a value and have it look like an estimate — so the confidence
    /// gate is applied here as well as where the numbers are printed, rather than trusted to have happened
    /// upstream.
    /// </remarks>
    public static string TryBuild(ChartDetails chart)
    {
        if (chart is null ||
            !string.Equals(chart.ValueConfidence, ChartValueConfidence.Exact, StringComparison.OrdinalIgnoreCase) ||
            chart.Series is not { Count: > 0 })
        {
            return null;
        }

        var datasets = new List<object>();

        foreach (var series in chart.Series)
        {
            if (series?.Points is not { Count: > 0 })
            {
                continue;
            }

            var points = series.Points
                .Where(point => point is { Length: >= 2 })
                .Select(point => new { x = point[0], y = point[1] })
                .ToList();

            if (points.Count == 0)
            {
                continue;
            }

            datasets.Add(new
            {
                // A series nobody named carries no label rather than an invented one. Chart.js falls back to
                // "Dataset 1", which says only which series it is -- and the legend is hidden below when no
                // series was named, so nothing claims a name the document did not print.
                label = string.IsNullOrWhiteSpace(series.Name) ? null : series.Name,
                data = points,
                showLine = true,
                fill = false,
            });
        }

        if (datasets.Count == 0)
        {
            return null;
        }

        var named = chart.Series.Any(series => !string.IsNullOrWhiteSpace(series?.Name));

        var config = new
        {
            // Scatter with the line shown, rather than a line chart: the points carry their own x values
            // read off the axis, and a line chart would space them evenly and quietly redraw the data.
            type = "scatter",
            data = new
            {
                datasets,
            },
            options = new
            {
                plugins = new
                {
                    legend = new
                    {
                        display = named,
                    },
                },
                scales = new
                {
                    x = AxisOptions(chart.AxisX),
                    y = AxisOptions(chart.AxisY),
                },
            },
        };

        return $"[chart:{JsonSerializer.Serialize(config, _options)}]";
    }

    private static object AxisOptions(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new { };
        }

        return new
        {
            title = new
            {
                display = true,
                text = title,
            },
        };
    }
}
