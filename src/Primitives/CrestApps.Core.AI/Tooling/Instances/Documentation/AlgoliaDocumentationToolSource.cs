using CrestApps.Core;
using CrestApps.Core.AI.Tooling;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// The built-in <see cref="IAIToolInstanceSource"/> that lets users configure documentation search through
/// the hosted Algolia DocSearch query API (the search used by many Docusaurus sites). Each configured
/// <see cref="AIToolInstance"/> binds one Algolia index; the AI model only supplies the search query.
/// </summary>
public sealed class AlgoliaDocumentationToolSource : IAIToolInstanceSource
{
    /// <summary>
    /// Creates the <see cref="DocumentationSearchToolFunction"/> bound to the supplied instance's settings.
    /// </summary>
    /// <param name="instance">The configured tool instance whose settings should be bound to the produced tool.</param>
    /// <returns>The configured documentation search function.</returns>
    public AITool CreateTool(AIToolInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var settings = instance.GetOrCreate<AlgoliaDocumentationToolSettings>();

        var functionName = instance.GetFunctionName();
        var description = string.IsNullOrWhiteSpace(instance.Description)
            ? "Searches the configured documentation site and returns the most relevant passages with their source URLs."
            : instance.Description;

        return new DocumentationSearchToolFunction(functionName, description, instance, services =>
        {
            var site = new AlgoliaDocSearchSite
            {
                Name = string.IsNullOrWhiteSpace(instance.Name)
                    ? functionName
                    : instance.Name,
                ApplicationId = settings.ApplicationId,
                ApiKey = UnprotectApiKey(settings.ApiKey, instance, services),
                IndexName = settings.IndexName,
                MaxResults = settings.MaxResults,
            };

            var options = services.GetService<IOptions<DocumentationSearchOptions>>()?.Value ?? new DocumentationSearchOptions();
            var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger<AlgoliaDocumentationSource>()
                ?? NullLogger<AlgoliaDocumentationSource>.Instance;

            return new AlgoliaDocumentationSource(site, options, httpClientFactory, logger);
        }, isLiveSearch: true);
    }

    private static string UnprotectApiKey(string apiKey, AIToolInstance instance, IServiceProvider services)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return apiKey;
        }

        var protector = services.GetService<IDataProtectionProvider>()
            ?.CreateProtector(DocumentationToolConstants.AlgoliaDataProtectionPurpose);

        if (protector is null)
        {
            return apiKey;
        }

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger<AlgoliaDocumentationToolSource>()
            ?? NullLogger<AlgoliaDocumentationToolSource>.Instance;

        return DataProtectionHelper.Unprotect(
            protector,
            apiKey,
            logger,
            "Failed to unprotect Algolia documentation API key for tool instance '{ToolInstanceId}'.",
            instance.ItemId);
    }
}
