using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrestApps.Core.AI.Tools;

/// <summary>
/// Builds a chart configuration directly from supplied data points.
/// <para>
/// When the caller already has the numbers, the configuration is assembled here rather than asked of a
/// model. Asking a model to restate dozens of values as prose and then re-derive them loses precision,
/// and a long series can exceed the response limit and come back as truncated, unparsable JSON. Built
/// here, a chart of any size is exact and cannot fail to parse.
/// </para>
/// </summary>
public static class ChartConfigurationBuilder
{
    /// <summary>
    /// The palette used when a series is not given an explicit color.
    /// </summary>
    private static readonly string[] _palette =
    [
        "#4E79A7",
        "#F28E2B",
        "#59A14F",
        "#E15759",
        "#B07AA1",
        "#76B7B2",
        "#EDC948",
        "#FF9DA7",
        "#9C755F",
        "#BAB0AC",
    ];

    private const string PositiveColor = "#59A14F";
    private const string NegativeColor = "#E15759";

    /// <summary>
    /// A single plotted series.
    /// </summary>
    /// <param name="Name">The series name shown in the legend.</param>
    /// <param name="Values">The values, aligned with the labels. A null entry leaves a gap.</param>
    public readonly record struct ChartSeries(string Name, IReadOnlyList<double?> Values);

    /// <summary>
    /// Builds the chart configuration.
    /// </summary>
    /// <param name="chartType">The chart type, for example <c>bar</c>, <c>line</c>, or <c>pie</c>.</param>
    /// <param name="title">The chart title.</param>
    /// <param name="labels">The category labels.</param>
    /// <param name="series">The plotted series.</param>
    /// <param name="horizontal">Whether a bar chart is drawn horizontally.</param>
    /// <param name="colorBySign">
    /// Whether each bar is colored by the sign of its value, which is what makes a variance chart
    /// readable at a glance.
    /// </param>
    /// <param name="stacked">Whether the series are stacked.</param>
    /// <returns>The serialized configuration.</returns>
    public static string Build(
        string chartType,
        string title,
        IReadOnlyList<string> labels,
        IReadOnlyList<ChartSeries> series,
        bool horizontal = false,
        bool colorBySign = false,
        bool stacked = false)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(series);

        var normalizedType = NormalizeType(chartType);
        var datasets = new JsonArray();

        for (var index = 0; index < series.Count; index++)
        {
            datasets.Add(BuildDataset(series[index], index, normalizedType, colorBySign, labels.Count));
        }

        var configuration = new JsonObject
        {
            ["type"] = normalizedType,
            ["data"] = new JsonObject
            {
                ["labels"] = new JsonArray(labels.Select(label => (JsonNode)JsonValue.Create(label ?? string.Empty)).ToArray()),
                ["datasets"] = datasets,
            },
            ["options"] = BuildOptions(normalizedType, title, series.Count, horizontal, stacked),
        };

        return configuration.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static JsonObject BuildDataset(
        ChartSeries series,
        int index,
        string chartType,
        bool colorBySign,
        int labelCount)
    {
        var values = new JsonArray();
        var colors = new JsonArray();
        var count = Math.Max(labelCount, series.Values?.Count ?? 0);

        for (var position = 0; position < count; position++)
        {
            var value = series.Values is not null && position < series.Values.Count
                ? series.Values[position]
                : null;

            values.Add(value is null ? null : JsonValue.Create(value.Value));

            if (colorBySign)
            {
                colors.Add(value is null || value.Value >= 0 ? PositiveColor : NegativeColor);
            }
        }

        var dataset = new JsonObject
        {
            ["label"] = series.Name ?? $"Series {(index + 1).ToString(CultureInfo.InvariantCulture)}",
            ["data"] = values,
        };

        // A pie chart colors each slice rather than the series as a whole, so it always needs the
        // per-point palette even when the caller did not ask for sign coloring.
        if (colorBySign)
        {
            dataset["backgroundColor"] = colors;
        }
        else if (chartType is "pie" or "doughnut" or "polarArea")
        {
            var sliceColors = new JsonArray();

            for (var position = 0; position < count; position++)
            {
                sliceColors.Add(_palette[position % _palette.Length]);
            }

            dataset["backgroundColor"] = sliceColors;
        }
        else
        {
            var color = _palette[index % _palette.Length];
            dataset["backgroundColor"] = color;
            dataset["borderColor"] = color;

            if (chartType == "line")
            {
                dataset["fill"] = false;
                dataset["tension"] = 0.2;
            }
        }

        return dataset;
    }

    private static JsonObject BuildOptions(
        string chartType,
        string title,
        int seriesCount,
        bool horizontal,
        bool stacked)
    {
        var plugins = new JsonObject
        {
            ["legend"] = new JsonObject
            {
                // One series needs no legend; its name is already the chart title's subject.
                ["display"] = seriesCount > 1 || chartType is "pie" or "doughnut" or "polarArea",
                ["position"] = "bottom",
            },
        };

        if (!string.IsNullOrWhiteSpace(title))
        {
            plugins["title"] = new JsonObject
            {
                ["display"] = true,
                ["text"] = title.Trim(),
            };
        }

        var options = new JsonObject
        {
            ["responsive"] = true,
            ["maintainAspectRatio"] = false,
            ["plugins"] = plugins,
        };

        if (horizontal && chartType == "bar")
        {
            options["indexAxis"] = "y";
        }

        if (chartType is not ("pie" or "doughnut" or "polarArea"))
        {
            options["scales"] = new JsonObject
            {
                ["x"] = new JsonObject { ["stacked"] = stacked },
                ["y"] = new JsonObject { ["stacked"] = stacked },
            };
        }

        return options;
    }

    private static string NormalizeType(string chartType)
    {
        if (string.IsNullOrWhiteSpace(chartType))
        {
            return "bar";
        }

        return chartType.Trim().ToLowerInvariant() switch
        {
            "line" => "line",
            "pie" => "pie",
            "doughnut" or "donut" => "doughnut",
            "radar" => "radar",
            "polararea" or "polar_area" or "polar" => "polarArea",
            "scatter" => "scatter",
            "bubble" => "bubble",
            _ => "bar",
        };
    }
}
