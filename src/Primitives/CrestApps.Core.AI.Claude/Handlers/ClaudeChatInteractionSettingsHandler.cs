using System.Text.Json;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Claude.Handlers;

internal sealed class ClaudeChatInteractionSettingsHandler : IChatInteractionSettingsHandler
{
    public Task UpdatingAsync(ChatInteraction interaction, JsonElement settings)
    {
        var orchestratorName = GetString(settings, "orchestratorName") ?? interaction.OrchestratorName;
        if (!string.Equals(orchestratorName, Services.ClaudeOrchestrator.OrchestratorName, StringComparison.OrdinalIgnoreCase))
        {
            interaction.Remove<ClaudeSessionMetadata>();
            return Task.CompletedTask;
        }

        interaction.Alter<ClaudeSessionMetadata>(metadata =>
        {
            metadata.ClaudeModel = GetString(settings, "anthropicModel");
        });

        return Task.CompletedTask;
    }

    public Task UpdatedAsync(ChatInteraction interaction, JsonElement settings)
    {
        return Task.CompletedTask;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }

        return null;
    }
}
