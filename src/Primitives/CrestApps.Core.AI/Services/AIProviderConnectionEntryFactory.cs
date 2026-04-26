using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Support;

namespace CrestApps.Core.AI.Services;

internal static class AIProviderConnectionEntryFactory
{
    /// <summary>
    /// Creates the operation.
    /// </summary>
    /// <param name="connection">The connection.</param>
    public static AIProviderConnectionEntry Create(AIProviderConnection connection)
    {
        return Create(connection, []);
    }

    /// <summary>
    /// Creates the operation.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="handlers">The handlers.</param>
    public static AIProviderConnectionEntry Create(AIProviderConnection connection, IEnumerable<IAIProviderConnectionHandler> handlers)
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

        foreach (var handler in handlers)
        {
            var context = new InitializingAIProviderConnectionContext(connection);

            handler.Initializing(context);

            foreach (var value in context.Values)
            {
                if (value.Value == null)
                {
                    continue;
                }

                values[value.Key] = value.Value;
            }
        }

        AIProviderConnectionDeploymentNameNormalizer.Normalize(values);

        values["DisplayText"] = string.IsNullOrWhiteSpace(connection.DisplayText)
            ? connection.Name
            : connection.DisplayText;

        return new AIProviderConnectionEntry(values);
    }
}
