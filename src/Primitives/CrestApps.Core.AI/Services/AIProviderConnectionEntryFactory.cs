using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Support;

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
                    ? jsonNode.GetRawValue()
                    : property.Value;
            }
        }

        values["DisplayText"] = string.IsNullOrWhiteSpace(connection.DisplayText)
            ? connection.Name
            : connection.DisplayText;

        return new AIProviderConnectionEntry(values);
    }
}
