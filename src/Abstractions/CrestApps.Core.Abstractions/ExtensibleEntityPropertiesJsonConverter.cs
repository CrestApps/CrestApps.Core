using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CrestApps.Core;

/// <summary>
/// Serializes and deserializes <see cref="ExtensibleEntity.Properties"/> as a normal nested
/// JSON object while keeping its values inside the property bag.
/// </summary>
internal sealed class ExtensibleEntityPropertiesJsonConverter : JsonConverter<IDictionary<string, object>>
{
    /// <summary>
    /// Reads the property bag from a nested JSON object.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The deserialized property bag.</returns>
    public override IDictionary<string, object> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        var node = JsonNode.Parse(ref reader)?.AsObject();

        if (node is null)
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in node)
        {
            properties[property.Key] = property.Value?.DeepClone();
        }

        return properties;
    }

    /// <summary>
    /// Writes the property bag as a nested JSON object.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The property bag value.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, IDictionary<string, object> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var property in value ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase))
        {
            writer.WritePropertyName(property.Key);
            WritePropertyValue(writer, property.Value);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a single property bag value using the extensible entity serializer options.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The property value.</param>
    private static void WritePropertyValue(Utf8JsonWriter writer, object value)
    {
        if (value is null)
        {
            writer.WriteNullValue();

            return;
        }

        if (value is JsonNode jsonNode)
        {
            jsonNode.WriteTo(writer, ExtensibleEntityExtensions.JsonSerializerOptions);

            return;
        }

        if (value is JsonElement jsonElement)
        {
            jsonElement.WriteTo(writer);

            return;
        }

        JsonSerializer.Serialize(writer, value, value.GetType(), ExtensibleEntityExtensions.JsonSerializerOptions);
    }
}
