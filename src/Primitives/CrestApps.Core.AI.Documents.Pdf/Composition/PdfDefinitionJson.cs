using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Reads and writes the composition model in the snake_case shape the PDF tools accept, and merges a partial
/// description into an existing one.
/// </summary>
/// <remarks>
/// The tools' arguments and the stored workspace use the same shape, so what a model sends in
/// <c>format_pdf</c> can be merged straight into what was stored last turn. A merge replaces values and
/// lists and descends into objects, which is exactly "change what I mention, keep the rest" — the behaviour
/// that lets a follow-up say only what changes.
/// </remarks>
internal static class PdfDefinitionJson
{
    /// <summary>
    /// Gets the serializer options for the composition model.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new LenientBooleanConverter(), new LenientNullableBooleanConverter(), new LenientStringConverter() },
    };

    /// <summary>
    /// Writes a model as a JSON object.
    /// </summary>
    /// <typeparam name="T">The model type.</typeparam>
    /// <param name="value">The model.</param>
    /// <returns>The JSON object.</returns>
    public static JsonObject ToNode<T>(T value)
    {
        return JsonSerializer.SerializeToNode(value, Options) as JsonObject ?? [];
    }

    /// <summary>
    /// Reads a model from JSON.
    /// </summary>
    /// <typeparam name="T">The model type.</typeparam>
    /// <param name="node">The JSON.</param>
    /// <returns>The model, or <see langword="null"/> when the JSON is empty.</returns>
    public static T FromNode<T>(JsonNode node)
    {
        return node is null
            ? default
            : node.Deserialize<T>(Options);
    }

    /// <summary>
    /// Reads a model from a JSON element.
    /// </summary>
    /// <typeparam name="T">The model type.</typeparam>
    /// <param name="element">The JSON element.</param>
    /// <returns>The model, or <see langword="null"/> when the element is not an object or an array.</returns>
    public static T FromElement<T>(JsonElement element)
    {
        return element.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            ? element.Deserialize<T>(Options)
            : default;
    }

    /// <summary>
    /// Merges a partial description into a model and returns the result.
    /// </summary>
    /// <typeparam name="T">The model type.</typeparam>
    /// <param name="target">The current model, or <see langword="null"/>.</param>
    /// <param name="patch">The partial description. A <see langword="null"/> value removes the property.</param>
    /// <returns>The merged model.</returns>
    public static T Merge<T>(T target, JsonElement patch)
        where T : class, new()
    {
        if (patch.ValueKind != JsonValueKind.Object)
        {
            return target;
        }

        var node = target is null
            ? []
            : ToNode(target);

        MergeInto(node, JsonNode.Parse(patch.GetRawText()) as JsonObject);

        return FromNode<T>(node) ?? new T();
    }

    /// <summary>
    /// Merges one JSON object into another in place: objects are merged recursively, everything else is
    /// replaced, and a <see langword="null"/> removes the property.
    /// </summary>
    /// <param name="target">The object merged into.</param>
    /// <param name="patch">The object merged from.</param>
    public static void MergeInto(JsonObject target, JsonObject patch)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (patch is null)
        {
            return;
        }

        foreach (var (key, value) in patch.ToList())
        {
            if (value is null)
            {
                target.Remove(key);

                continue;
            }

            if (value is JsonObject patchObject && target[key] is JsonObject targetObject)
            {
                MergeInto(targetObject, patchObject);

                continue;
            }

            target[key] = value.DeepClone();
        }
    }

    /// <summary>
    /// Reads a boolean that a model may have written as a string or a number.
    /// </summary>
    private sealed class LenientBooleanConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return ReadBoolean(ref reader) ?? false;
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            writer.WriteBooleanValue(value);
        }
    }

    /// <summary>
    /// Reads a nullable boolean that a model may have written as a string or a number.
    /// </summary>
    private sealed class LenientNullableBooleanConverter : JsonConverter<bool?>
    {
        public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return ReadBoolean(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteBooleanValue(value.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    /// <summary>
    /// Reads a string that a model may have written as a number or a boolean, such as a table cell.
    /// </summary>
    private sealed class LenientStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => ReadNumberText(ref reader),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Null => null,
                _ => ReadRaw(ref reader),
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }

        private static string ReadNumberText(ref Utf8JsonReader reader)
        {
            return reader.HasValueSequence
                ? System.Text.Encoding.UTF8.GetString(System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence))
                : System.Text.Encoding.UTF8.GetString(reader.ValueSpan);
        }

        private static string ReadRaw(ref Utf8JsonReader reader)
        {
            using var document = JsonDocument.ParseValue(ref reader);

            return document.RootElement.GetRawText();
        }
    }

    private static bool? ReadBoolean(ref Utf8JsonReader reader)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => reader.TryGetDouble(out var number) && number != 0,
            JsonTokenType.String => bool.TryParse(reader.GetString(), out var parsed)
                ? parsed
                : reader.GetString() is "1" or "yes" or "on",
            _ => null,
        };
    }
}
