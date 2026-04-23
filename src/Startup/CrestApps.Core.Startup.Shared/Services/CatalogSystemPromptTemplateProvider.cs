using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Providers;

namespace CrestApps.Core.Startup.Shared.Services;

/// <summary>
/// Exposes UI-created system prompt templates from the catalog through the generic template service.
/// </summary>
public sealed class CatalogSystemPromptTemplateProvider : ITemplateProvider
{
    private const string ProviderSource = "Catalog";

    private readonly ICatalog<AIProfileTemplate> _catalog;

    public CatalogSystemPromptTemplateProvider(ICatalog<AIProfileTemplate> catalog)
    {
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<Template>> GetTemplatesAsync()
    {
        var templates = await _catalog.GetAllAsync();

        return templates
            .Where(static template => template.IsListable)
            .Where(static template => string.Equals(template.Source, AITemplateSources.SystemPrompt, StringComparison.OrdinalIgnoreCase))
            .Where(template => template.TryGet<SystemPromptTemplateMetadata>(out var metadata) && !string.IsNullOrWhiteSpace(metadata.SystemMessage))
            .Select(template =>
            {
                template.TryGet<SystemPromptTemplateMetadata>(out var metadata);

                return new Template
                {
                    Id = BuildTemplateId(template),
                    Source = ProviderSource,
                    Content = metadata.SystemMessage,
                    Metadata = new TemplateMetadata
                    {
                        Title = template.DisplayText ?? template.Name,
                        Description = template.Description,
                        Category = template.Category,
                        IsListable = template.IsListable,
                    },
                };
            })
            .ToList();
    }

    private static string BuildTemplateId(AIProfileTemplate template)
    {
        if (!string.IsNullOrWhiteSpace(template.ItemId))
        {
            return $"catalog:{template.ItemId}";
        }

        return $"catalog:{template.Name}";
    }
}
