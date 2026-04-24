using System.Text.Json;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Startup.Shared.Services;

public sealed class DocumentChatInteractionSettingsHandler : IChatInteractionSettingsHandler
{
    public Task UpdatingAsync(ChatInteraction interaction, JsonElement settings, CancellationToken cancellationToken = default)
    {
        interaction.Alter<DocumentsMetadata>(metadata =>
        {
            metadata.RetrievalMode = GetRetrievalMode(settings, "documentRetrievalMode");
        });

        return Task.CompletedTask;
    }

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
