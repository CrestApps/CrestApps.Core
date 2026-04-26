using System.Text.Json;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Claude.Handlers;

internal sealed class ClaudeChatInteractionSettingsHandler : IChatInteractionSettingsHandler
{
    /// <summary>
    /// Updatings the operation.
    /// </summary>
    /// <param name="interaction">The interaction.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task UpdatingAsync(ChatInteraction interaction, JsonElement settings, CancellationToken cancellationToken = default)
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
            metadata.EffortLevel = GetEnum<ClaudeEffortLevel>(settings, "anthropicEffortLevel");
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Updateds the operation.
    /// </summary>
    /// <param name="interaction">The interaction.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task UpdatedAsync(ChatInteraction interaction, JsonElement settings, CancellationToken cancellationToken = default)
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

    private static T GetEnum<T>(JsonElement element, string propertyName) where T : struct, Enum
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && Enum.IsDefined(typeof(T), prop.GetInt32()))
            {
                return (T)(object)prop.GetInt32();
            }

            if (prop.ValueKind == JsonValueKind.String && Enum.TryParse<T>(prop.GetString(), ignoreCase: true, out var result))
            {
                return result;
            }
        }

        return default;
    }
}
