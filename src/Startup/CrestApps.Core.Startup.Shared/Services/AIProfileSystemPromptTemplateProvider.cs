using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Providers;

namespace CrestApps.Core.Startup.Shared.Services;

/// <summary>
/// Exposes system prompt AI profile templates through the generic template service.
/// </summary>
public sealed class AIProfileSystemPromptTemplateProvider : ITemplateProvider
{
    private const string ProviderSource = "AIProfileTemplate";

    private readonly IAIProfileTemplateManager _templateManager;

    public AIProfileSystemPromptTemplateProvider(IAIProfileTemplateManager templateManager)
    {
        _templateManager = templateManager;
    }

    public async Task<IReadOnlyList<Template>> GetTemplatesAsync()
    {
        var templates = await _templateManager.GetAllAsync();

return (templates ?? [])
            .Where(IsSystemPromptTemplate)
            .Select(MapTemplate)
            .Where(template => !string.IsNullOrWhiteSpace(template.Content))
            .ToList();
    }

    private static bool IsSystemPromptTemplate(AIProfileTemplate template)
    {
        return template != null &&
            string.Equals(template.Source, AITemplateSources.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    private static Template MapTemplate(AIProfileTemplate template)
    {
        template.TryGet<SystemPromptTemplateMetadata>(out var metadata);

return new Template
        {
            Id = BuildTemplateId(template),
            Kind = template.Source,
            Source = ProviderSource,
            Content = metadata?.SystemMessage,
            Metadata = new TemplateMetadata
            {
                Title = template.DisplayText ?? template.Name,
                Description = template.Description,
                Category = template.Category,
                IsListable = template.IsListable,
            },
        };
    }

    private static string BuildTemplateId(AIProfileTemplate template)
    {
        if (!string.IsNullOrWhiteSpace(template.ItemId))
        {
            return $"ai-profile:{template.ItemId}";
        }

        return $"ai-profile:{template.Name}";
    }
}
