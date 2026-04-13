using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Json;

public sealed class AIProviderConnectionJsonConverter : JsonConverter<AIProviderConnection>
{
    public override AIProviderConnection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader)?.AsObject();

        if (node == null)
        {
            return null;
        }

        var connection = new AIProviderConnection
        {
            ItemId = GetString(node, nameof(AIProviderConnection.ItemId)),
            Source = GetString(node, nameof(AIProviderConnection.Source))
            ?? GetString(node, nameof(AIProviderConnection.ClientName))
            ?? GetString(node, "ProviderName"),
            Name = GetString(node, nameof(AIProviderConnection.Name)),
            DisplayText = GetString(node, nameof(AIProviderConnection.DisplayText)),
            IsReadOnly = GetBoolean(node, nameof(AIProviderConnection.IsReadOnly)),
            CreatedUtc = GetDateTime(node, nameof(AIProviderConnection.CreatedUtc)),
            Author = GetString(node, nameof(AIProviderConnection.Author)),
            OwnerId = GetString(node, nameof(AIProviderConnection.OwnerId)),
        };

        var propertyValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (node.TryGetPropertyValue(nameof(AIProviderConnection.Properties), out var propertiesNode)
            && propertiesNode is JsonObject propertiesObject)
        {
            // Detach from parent before deserializing.
            node.Remove(nameof(AIProviderConnection.Properties));
            foreach (var property in propertiesObject.Deserialize<Dictionary<string, object>>() ?? new Dictionary<string, object>())
            {
                propertyValues[property.Key] = property.Value;
            }
        }

        if (propertyValues.Count > 0)
        {
            connection.Properties = propertyValues;
        }

        return connection;
    }

    public override void Write(Utf8JsonWriter writer, AIProviderConnection value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        WriteString(writer, nameof(AIProviderConnection.ItemId), value.ItemId);
        WriteString(writer, nameof(AIProviderConnection.ClientName), value.ClientName);
        WriteString(writer, nameof(AIProviderConnection.Name), value.Name);
        WriteString(writer, nameof(AIProviderConnection.DisplayText), value.DisplayText);
        writer.WriteBoolean(nameof(AIProviderConnection.IsReadOnly), value.IsReadOnly);
        writer.WriteString(nameof(AIProviderConnection.CreatedUtc), value.CreatedUtc);
        WriteString(writer, nameof(AIProviderConnection.Author), value.Author);
        WriteString(writer, nameof(AIProviderConnection.OwnerId), value.OwnerId);

        writer.WritePropertyName(nameof(AIProviderConnection.Properties));

        if (value.Properties != null)
        {
            JsonSerializer.Serialize(writer, value.Properties, options);
        }
        else
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static string GetString(JsonObject node, string name)
    {
        if (node.TryGetPropertyValue(name, out var value) && value != null)
        {
            return value.GetValue<string>();
        }

        return null;
    }

    private static bool GetBoolean(JsonObject node, string name)
    {
        if (node.TryGetPropertyValue(name, out var value) && value != null)
        {
            return value.GetValue<bool>();
        }

        return false;
    }

    private static DateTime GetDateTime(JsonObject node, string name)
    {
        if (node.TryGetPropertyValue(name, out var value) && value != null)
        {
            return value.GetValue<DateTime>();
        }

        return default;
    }

    private static void WriteString(Utf8JsonWriter writer, string name, string value)
    {
        if (value != null)
        {
            writer.WriteString(name, value);
        }
        else
        {
            writer.WriteNull(name);
        }
    }
}
