using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CrestApps.Core;

/// <summary>
/// Base class for entities that support dynamic extensible properties.
/// </summary>
public abstract class ExtensibleEntity : IJsonOnDeserialized, IJsonOnSerialized, IJsonOnSerializing
{
    private IDictionary<string, object> _serializedProperties;

    /// <summary>
    /// Gets or sets the dictionary of additional properties that are not explicitly
    /// declared on the entity.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();

    void IJsonOnDeserialized.OnDeserialized()
    {
       var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

       if (Properties.TryGetValue(nameof(Properties), out var propertyValue))
       {
           MergeNestedProperties(properties, propertyValue);
       }

       Properties = properties;
    }

    void IJsonOnSerializing.OnSerializing()
    {
        _serializedProperties = Properties ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        Properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(Properties)] = JsonSerializer.SerializeToNode(_serializedProperties, ExtensibleEntityExtensions.JsonSerializerOptions)?.DeepClone() as JsonObject ?? [],
        };
    }

    void IJsonOnSerialized.OnSerialized()
    {
        Properties = _serializedProperties ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        _serializedProperties = null;
    }

    private static void MergeNestedProperties(
        Dictionary<string, object> properties,
        object propertyValue)
    {
        if (propertyValue is null)
        {
            return;
        }

        if (propertyValue is JsonObject propertyObject)
        {
            foreach (var property in propertyObject)
            {
                properties[property.Key] = property.Value?.DeepClone();
            }

            return;
        }

        if (propertyValue is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in jsonElement.EnumerateObject())
            {
                properties[property.Name] = property.Value.Clone();
            }

            return;
        }

        if (propertyValue is IDictionary<string, object> propertyDictionary)
        {
            foreach (var property in propertyDictionary)
            {
                properties[property.Key] = CloneExtensionValue(property.Value);
            }

            return;
        }

        throw new JsonException($"'{nameof(Properties)}' must be a JSON object.");
    }

    private static object CloneExtensionValue(object value)
    {
        return value switch
        {
            null => null,
            JsonNode jsonNode => jsonNode.DeepClone(),
            JsonElement jsonElement => jsonElement.Clone(),
            _ => value,
        };
    }
}
