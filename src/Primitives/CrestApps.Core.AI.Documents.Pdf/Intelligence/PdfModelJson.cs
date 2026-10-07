using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.Support.Json;

namespace CrestApps.Core.AI.Documents.Pdf.Intelligence;

/// <summary>
/// Reads the JSON a model was asked for out of what it actually wrote: fenced, wrapped in prose, or bare.
/// </summary>
internal static class PdfModelJson
{
    /// <summary>
    /// Reads a JSON object out of a model's answer.
    /// </summary>
    /// <param name="text">The answer.</param>
    /// <returns>The object, or <see langword="null"/> when the answer holds none.</returns>
    public static JsonObject ParseObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var candidates = new List<string>
        {
            JsonExtractor.ExtractJsonObject(text),
            JsonExtractor.ExtractFromCodeFence(text),
            text.Trim(),
        };

        foreach (var candidate in candidates)
        {
            if (TryParse(candidate) is JsonObject parsed)
            {
                return parsed;
            }
        }

        // A model asked for an object sometimes answers with the array the object would have held.
        if (TryParse(ExtractArray(text)) is JsonArray array)
        {
            return new JsonObject
            {
                ["items"] = array,
            };
        }

        return null;
    }

    /// <summary>
    /// Reads a node as text, whatever JSON type the model wrote it as.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The text, or <see langword="null"/> when the node is missing, null or blank.</returns>
    public static string GetText(JsonNode node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }

            return value.GetValueKind() switch
            {
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToJsonString(),
                _ => null,
            };
        }

        return null;
    }

    /// <summary>
    /// Reads a node as a number.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The number, or <see langword="null"/>.</returns>
    public static double? GetNumber(JsonNode node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<double>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) &&
            double.TryParse(text.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    /// <summary>
    /// Reads the page numbers a model gave for a value: a number, an array of numbers, or text such as
    /// <c>"3, 5"</c>.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="pageCount">The number of pages, so impossible numbers are dropped.</param>
    /// <returns>The distinct, sorted pages.</returns>
    public static List<int> GetPages(JsonNode node, int pageCount)
    {
        var pages = new SortedSet<int>();

        void Add(double? number)
        {
            if (number is { } value && value >= 1 && value <= pageCount && value == Math.Floor(value))
            {
                pages.Add((int)value);
            }
        }

        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    Add(GetNumber(item));
                }

                break;

            case JsonValue value when value.GetValueKind() == JsonValueKind.Number:
                Add(GetNumber(value));

                break;

            case JsonValue value when value.TryGetValue<string>(out var text):
                foreach (var part in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (double.TryParse(part.TrimStart('p', 'P', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    {
                        Add(parsed);
                    }
                }

                break;
        }

        return [.. pages];
    }

    private static JsonNode TryParse(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        try
        {
            // A model does not always keep to the casing it was shown, so names are matched either way.
            var node = JsonNode.Parse(
                candidate,
                new JsonNodeOptions
                {
                    PropertyNameCaseInsensitive = true,
                },
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });

            // Reading it through once surfaces a repeated property name here rather than on first use.
            _ = node?.ToJsonString();

            return node;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string ExtractArray(string text)
    {
        var start = text.IndexOf('[', StringComparison.Ordinal);
        var end = text.LastIndexOf(']');

        return start >= 0 && end > start
            ? text[start..(end + 1)]
            : null;
    }
}
