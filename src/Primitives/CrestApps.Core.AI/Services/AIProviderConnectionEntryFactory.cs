using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

internal static class AIProviderConnectionEntryFactory
{
    public static AIProviderConnectionEntry Create(AIProviderConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (connection.Properties != null)
        {
            foreach (var property in connection.Properties)
            {
                values[property.Key] = property.Value is JsonNode jsonNode
                    ? ConvertJsonNode(jsonNode)
                    : property.Value;
            }
        }

        values["DisplayText"] = string.IsNullOrWhiteSpace(connection.DisplayText)
            ? connection.Name
            : connection.DisplayText;

        return new AIProviderConnectionEntry(values);
    }

    private static object ConvertJsonNode(JsonNode node)
    {
        return node switch
        {
            JsonObject jsonObject => jsonObject.ToDictionary(
                property => property.Key,
                property => ConvertJsonNode(property.Value),
                StringComparer.OrdinalIgnoreCase),
            JsonArray jsonArray => jsonArray.Select(ConvertJsonNode).ToList(),
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out var s) => s,
            JsonValue jsonValue when jsonValue.TryGetValue<bool>(out var b) => b,
            JsonValue jsonValue when jsonValue.TryGetValue<int>(out var i) => i,
            JsonValue jsonValue when jsonValue.TryGetValue<long>(out var l) => l,
            JsonValue jsonValue when jsonValue.TryGetValue<double>(out var d) => d,
            _ => node?.ToString(),
        };
    }
}
