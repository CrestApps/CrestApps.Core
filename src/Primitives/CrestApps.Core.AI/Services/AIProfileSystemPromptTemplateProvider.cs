using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Services;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Exposes the <see cref="AITemplateSources.SystemPrompt"/> AI profile templates through the template
/// service, so a prompt template selected on an AI profile or chat interaction renders at completion time.
/// </summary>
/// <remarks>
/// A template's identifier is its name. Its earlier identifiers, <c>ai-profile:{ItemId}</c> and
/// <c>ai-profile:{Name}</c>, are kept as aliases so stored selections still resolve.
/// The templates come from the <see cref="IAIProfileTemplateManager"/>, so they can be edited at any time;
/// as an <see cref="IDynamicTemplateProvider"/>, they are read again in every scope.
/// When the host registers no AI profile template store, the provider returns no templates.
/// </remarks>
public sealed class AIProfileSystemPromptTemplateProvider : IDynamicTemplateProvider
{
    /// <summary>
    /// The <see cref="Template.Source"/> of the templates this provider returns.
    /// </summary>
    public const string ProviderSource = "AIProfileTemplate";

    private const string AliasPrefix = "ai-profile:";

    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIProfileSystemPromptTemplateProvider"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve the optional AI profile template store.</param>
    public AIProfileSystemPromptTemplateProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets the system prompt AI profile templates.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One template per named system prompt AI profile template.</returns>
    public async Task<IReadOnlyList<Template>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        if (_serviceProvider.GetService<INamedSourceCatalog<AIProfileTemplate>>() is null)
        {
            return [];
        }

        var templateManager = _serviceProvider.GetRequiredService<IAIProfileTemplateManager>();
        var templates = await templateManager.GetAllAsync(cancellationToken);

        return (templates ?? [])
            .Where(IsSystemPromptTemplate)
            .Select(MapTemplate)
            .ToList();
    }

    private static bool IsSystemPromptTemplate(AIProfileTemplate template)
    {
        return template != null &&
            !string.IsNullOrWhiteSpace(template.Name) &&
            string.Equals(template.Source, AITemplateSources.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    private static Template MapTemplate(AIProfileTemplate template)
    {
        template.TryGet<SystemPromptTemplateMetadata>(out var metadata);

        return new Template
        {
            Id = template.Name,
            Aliases = BuildAliases(template),
            Kind = template.Source,
            Source = ProviderSource,
            Content = metadata?.SystemMessage ?? string.Empty,
            Metadata = new TemplateMetadata
            {
                Title = string.IsNullOrWhiteSpace(template.DisplayText)
                    ? template.Name
                    : template.DisplayText,
                Description = template.Description,
                Category = template.Category,
                IsListable = template.IsListable,
            },
        };
    }

    private static string[] BuildAliases(AIProfileTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.ItemId))
        {
            return [AliasPrefix + template.Name];
        }

        return [AliasPrefix + template.ItemId, AliasPrefix + template.Name];
    }
}
