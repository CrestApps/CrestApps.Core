using System.Text.Json;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat.Handlers;

/// <summary>
/// Saves the persona a chat interaction links to from the <c>personaId</c> setting sent by the client.
/// </summary>
public sealed class PersonaChatInteractionSettingsHandler : IChatInteractionSettingsHandler
{
    /// <summary>
    /// Stores the chosen persona on the interaction. A client that does not send <c>personaId</c> at all leaves the
    /// current link untouched, so a host without a persona picker never clears one set elsewhere.
    /// </summary>
    /// <param name="interaction">The interaction being updated.</param>
    /// <param name="settings">The settings sent by the client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task UpdatingAsync(ChatInteraction interaction, JsonElement settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        if (settings.ValueKind != JsonValueKind.Object ||
            !settings.TryGetProperty("personaId", out var personaId))
        {
            return Task.CompletedTask;
        }

        var value = personaId.ValueKind == JsonValueKind.String
            ? personaId.GetString()?.Trim()
            : null;

        interaction.Alter<AIPersonaMetadata>(metadata =>
        {
            metadata.PersonaId = string.IsNullOrEmpty(value)
                ? null
                : value;
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs after the interaction is saved; nothing to do.
    /// </summary>
    /// <param name="interaction">The interaction that was updated.</param>
    /// <param name="settings">The settings sent by the client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task UpdatedAsync(ChatInteraction interaction, JsonElement settings, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
