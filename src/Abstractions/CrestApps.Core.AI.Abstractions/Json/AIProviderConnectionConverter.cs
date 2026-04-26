using System.Text.Json;
using System.Text.Json.Serialization;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Json;

/// <summary>
/// A <see cref="JsonConverter{T}"/> for <see cref="AIProviderConnectionEntry"/> that
/// serializes the entry as a flat key-value dictionary.
/// </summary>
public sealed class AIProviderConnectionConverter : JsonConverter<AIProviderConnectionEntry>
{
    /// <summary>
    /// Reads the operation.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <param name="options">The options.</param>
    public override AIProviderConnectionEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Deserialize into a dictionary first.
        var dictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(ref reader, options);

        if (dictionary is null)
        {
            return null;
        }

        return new AIProviderConnectionEntry(dictionary);
    }

    /// <summary>
    /// Writes the operation.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The value.</param>
    /// <param name="options">The options.</param>
    public override void Write(Utf8JsonWriter writer, AIProviderConnectionEntry value, JsonSerializerOptions options)
    {
        // Serialize as dictionary.
        JsonSerializer.Serialize(writer, (IDictionary<string, object>)value, options);
    }
}
