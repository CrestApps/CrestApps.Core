using System.Text.Json;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Services;

namespace CrestApps.Core.Mvc.Web.Areas.ChatInteractions.Handlers;

/// <summary>
/// Persists AI tool instance selections from chat interaction settings updates.
/// </summary>
public sealed class AIToolInstanceChatInteractionSettingsHandler : IChatInteractionSettingsHandler
{
    private readonly ISourceCatalog<AIToolInstance> _toolInstanceCatalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolInstanceChatInteractionSettingsHandler"/> class.
    /// </summary>
    /// <param name="toolInstanceCatalog">The AI tool instance catalog.</param>
    public AIToolInstanceChatInteractionSettingsHandler(ISourceCatalog<AIToolInstance> toolInstanceCatalog)
    {
        _toolInstanceCatalog = toolInstanceCatalog;
    }

    /// <summary>
    /// Applies selected AI tool instance names to the interaction metadata.
    /// </summary>
    /// <param name="interaction">The interaction being updated.</param>
    /// <param name="settings">The raw settings payload from the client.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async Task UpdatingAsync(
        ChatInteraction interaction,
        JsonElement settings,
        CancellationToken cancellationToken = default)
    {
        var toolInstanceNames = await GetValidToolInstanceNamesAsync(GetStringArray(settings, "toolInstanceNames"));

        cancellationToken.ThrowIfCancellationRequested();

        interaction.Alter<AIToolInstanceMetadata>(metadata =>
        {
            metadata.ToolInstanceNames = toolInstanceNames;
        });
    }

    /// <summary>
    /// Handles post-save chat interaction settings updates.
    /// </summary>
    /// <param name="interaction">The updated interaction.</param>
    /// <param name="settings">The raw settings payload from the client.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public Task UpdatedAsync(
        ChatInteraction interaction,
        JsonElement settings,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Filters selected tool instance names to the names that still exist in the catalog.
    /// </summary>
    /// <param name="selectedNames">The selected tool instance names.</param>
    /// <returns>The valid selected tool instance names.</returns>
    private async Task<string[]> GetValidToolInstanceNamesAsync(IEnumerable<string> selectedNames)
    {
        var validToolInstanceNames = (await _toolInstanceCatalog.GetAllAsync())
            .Select(instance => instance.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return (selectedNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name) && validToolInstanceNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Reads a string array from the settings payload.
    /// </summary>
    /// <param name="settings">The raw settings payload from the client.</param>
    /// <param name="propertyName">The property name to read.</param>
    /// <returns>The string array value, or an empty array when the property is absent.</returns>
    private static string[] GetStringArray(JsonElement settings, string propertyName)
    {
        if (!settings.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }
}
