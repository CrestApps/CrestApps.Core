using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Profiles;

/// <summary>
/// Extension methods for working with AI profile templates.
/// </summary>
public static class AIProfileTemplateManagerExtensions
{
    private const string SystemPromptSource = "SystemPrompt";

    /// <summary>
    /// Gets listable system prompt templates that contain a non-empty system message.
    /// </summary>
    /// <param name="templateManager">The AI profile template manager.</param>
    public static async ValueTask<IEnumerable<AIProfileTemplate>> GetListableTemplatesAsync(this IAIProfileTemplateManager templateManager)
    {
        ArgumentNullException.ThrowIfNull(templateManager);

        var templates = await templateManager.GetListableAsync();

        return (templates ?? [])
            .Where(static template => template.IsListable)
            .Where(static template => string.Equals(template.Source, SystemPromptSource, StringComparison.OrdinalIgnoreCase))
            .Where(IsSystemMessage);
    }

    private static bool IsSystemMessage(AIProfileTemplate template)
    {
        if (template == null ||
            !string.Equals(template.Source, SystemPromptSource, StringComparison.OrdinalIgnoreCase) ||
            !template.TryGet<SystemPromptTemplateMetadata>(out var metadata) ||
            string.IsNullOrWhiteSpace(metadata.SystemMessage))
        {
            return false;
        }

        return true;
    }
}
