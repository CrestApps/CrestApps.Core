using System.Globalization;
using System.Text.Json;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Turns a Chart.js configuration — what <c>generate_chart</c> returns inside its <c>[chart:…]</c> marker —
/// into a chart a PDF can draw.
/// </summary>
/// <remarks>
/// A chart the user already saw in the conversation is the one they expect in the document. Reading the
/// configuration that drew it keeps its labels, values, series names and colours, instead of asking the model
/// to restate every number a second time.
/// </remarks>
internal static class PdfChartJsReader
{
    private const string MarkerPrefix = "[chart:";

    /// <summary>
    /// Reads a Chart.js configuration.
    /// </summary>
    /// <param name="configuration">The configuration JSON, or a whole <c>[chart:…]</c> marker.</param>
    /// <param name="chart">The chart, when the configuration could be read.</param>
    /// <returns><see langword="true"/> when the configuration held plottable data.</returns>
    public static bool TryRead(string configuration, out PdfChartDefinition chart)
    {
        chart = null;

        if (string.IsNullOrWhiteSpace(configuration))
        {
            return false;
        }

        var json = configuration.Trim();

        if (json.StartsWith(MarkerPrefix, StringComparison.OrdinalIgnoreCase) && json.EndsWith(']'))
        {
            json = json[MarkerPrefix.Length..^1];
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            return TryRead(document.RootElement, out chart);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a Chart.js configuration element.
    /// </summary>
    /// <param name="root">The configuration.</param>
    /// <param name="chart">The chart, when the configuration could be read.</param>
    /// <returns><see langword="true"/> when the configuration held plottable data.</returns>
    public static bool TryRead(JsonElement root, out PdfChartDefinition chart)
    {
        chart = null;

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var type = root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()
            : "bar";

        root.TryGetProperty("options", out var options);

        var horizontal = options.ValueKind == JsonValueKind.Object &&
            options.TryGetProperty("indexAxis", out var indexAxis) &&
            string.Equals(indexAxis.GetString(), "y", StringComparison.OrdinalIgnoreCase);

        var stacked = IsStacked(options);

        chart = new PdfChartDefinition
        {
            ChartType = MapType(type, horizontal, stacked),
            Title = ReadTitle(options),
        };

        if (data.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
        {
            foreach (var label in labels.EnumerateArray())
            {
                chart.Labels.Add(label.ValueKind == JsonValueKind.String ? label.GetString() : label.GetRawText());
            }
        }

        if (data.TryGetProperty("datasets", out var datasets) && datasets.ValueKind == JsonValueKind.Array)
        {
            foreach (var dataset in datasets.EnumerateArray())
            {
                if (dataset.ValueKind != JsonValueKind.Object ||
                    !dataset.TryGetProperty("data", out var values) ||
                    values.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var series = new PdfChartSeriesDefinition
                {
                    Name = dataset.TryGetProperty("label", out var name) && name.ValueKind == JsonValueKind.String
                        ? name.GetString()
                        : null,
                    Color = ReadColor(dataset),
                };

                foreach (var value in values.EnumerateArray())
                {
                    series.Values.Add(ReadNumber(value));
                }

                chart.Series.Add(series);
            }
        }

        return chart.Series.Count > 0 && chart.Series.Any(series => series.Values.Count > 0);
    }

    private static string MapType(string type, bool horizontal, bool stacked)
    {
        return type?.ToLowerInvariant() switch
        {
            "line" => "line",
            "pie" or "doughnut" or "polararea" => "pie",
            "radar" => "line",
            "area" => "area",
            _ => horizontal
                ? stacked ? "stacked_horizontal_bar" : "horizontal_bar"
                : stacked ? "stacked_column" : "column",
        };
    }

    private static bool IsStacked(JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Object ||
            !options.TryGetProperty("scales", out var scales) ||
            scales.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var axis in scales.EnumerateObject())
        {
            if (axis.Value.ValueKind == JsonValueKind.Object &&
                axis.Value.TryGetProperty("stacked", out var stacked) &&
                stacked.ValueKind == JsonValueKind.True)
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadTitle(JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Object ||
            !options.TryGetProperty("plugins", out var plugins) ||
            plugins.ValueKind != JsonValueKind.Object ||
            !plugins.TryGetProperty("title", out var title) ||
            title.ValueKind != JsonValueKind.Object ||
            !title.TryGetProperty("text", out var text))
        {
            return null;
        }

        return text.ValueKind switch
        {
            JsonValueKind.String => text.GetString(),
            JsonValueKind.Array => string.Join(" ", text.EnumerateArray().Select(part => part.ToString())),
            _ => null,
        };
    }

    private static string ReadColor(JsonElement dataset)
    {
        foreach (var name in new[] { "backgroundColor", "borderColor" })
        {
            if (!dataset.TryGetProperty(name, out var color))
            {
                continue;
            }

            // One colour per point is a pie's palette; the series colour is its first entry.
            var value = color.ValueKind switch
            {
                JsonValueKind.String => color.GetString(),
                JsonValueKind.Array when color.GetArrayLength() > 0 && color[0].ValueKind == JsonValueKind.String => color[0].GetString(),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(value))
            {
                return StripAlpha(value);
            }
        }

        return null;
    }

    private static string StripAlpha(string color)
    {
        // Chart.js colours are often rgba() with a translucent fill; the printed series is drawn solid.
        if (color.StartsWith("rgba", StringComparison.OrdinalIgnoreCase))
        {
            var open = color.IndexOf('(', StringComparison.Ordinal);
            var parts = color[(open + 1)..].TrimEnd(')').Split(',');

            if (parts.Length >= 3)
            {
                return $"rgb({parts[0]},{parts[1]},{parts[2]})";
            }
        }

        return color;
    }

    private static double? ReadNumber(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,

            // A scatter or bubble point carries its value on the y axis.
            JsonValueKind.Object when value.TryGetProperty("y", out var y) && y.TryGetDouble(out var point) => point,
            _ => null,
        };
    }
}
