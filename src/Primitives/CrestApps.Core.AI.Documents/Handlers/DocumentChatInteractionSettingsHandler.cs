using System.Text.Json;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Handlers;

/// <summary>
/// Applies document-specific chat interaction settings before an interaction is persisted.
/// </summary>
public sealed class DocumentChatInteractionSettingsHandler : IChatInteractionSettingsHandler
{
    /// <summary>
    /// Applies document retrieval settings from the raw client payload to the interaction metadata.
    /// </summary>
    /// <param name="interaction">The chat interaction being updated.</param>
    /// <param name="settings">The raw settings payload from the client.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public Task UpdatingAsync(ChatInteraction interaction, JsonElement settings, CancellationToken cancellationToken = default)
    {
        interaction.Alter<DocumentsMetadata>(metadata =>
        {
            metadata.RetrievalMode = GetRetrievalMode(settings, "documentRetrievalMode");
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs after the interaction has been persisted.
    /// </summary>
    /// <param name="interaction">The updated chat interaction.</param>
    /// <param name="settings">The raw settings payload from the client.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public Task UpdatedAsync(ChatInteraction interaction, JsonElement settings, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private static DocumentRetrievalMode? GetRetrievalMode(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();

            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (Enum.TryParse<DocumentRetrievalMode>(value, ignoreCase: true, out var mode))
            {
                return mode;
            }
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var numericValue) &&
            Enum.IsDefined(typeof(DocumentRetrievalMode), numericValue))
        {
            return (DocumentRetrievalMode)numericValue;
        }

        return null;
    }
}
