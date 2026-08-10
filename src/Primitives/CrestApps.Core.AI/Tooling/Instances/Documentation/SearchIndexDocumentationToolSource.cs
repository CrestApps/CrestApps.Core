using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// The built-in <see cref="IAIToolInstanceSource"/> that lets users configure documentation search over a
/// single site that publishes a prebuilt JSON search index (for example a MkDocs Material
/// <c>search_index.json</c>). Each configured <see cref="AIToolInstance"/> binds one site; the AI model
/// only supplies the search query.
/// </summary>
public sealed class SearchIndexDocumentationToolSource : IAIToolInstanceSource
{
    /// <summary>
    /// Creates the <see cref="DocumentationSearchToolFunction"/> bound to the supplied instance's settings.
    /// </summary>
    /// <param name="instance">The configured tool instance whose settings should be bound to the produced tool.</param>
    /// <returns>The configured documentation search function.</returns>
    public AITool CreateTool(AIToolInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var settings = instance.TryGet<SearchIndexDocumentationToolSettings>(out var stored)
            ? stored
            : new SearchIndexDocumentationToolSettings();

        var functionName = instance.GetFunctionName();
        var description = string.IsNullOrWhiteSpace(instance.Description)
            ? "Searches the configured documentation site and returns the most relevant passages with their source URLs."
            : instance.Description;

        return new DocumentationSearchToolFunction(functionName, description, instance, services =>
        {
            var site = new DocumentationSearchIndexSite
            {
                Name = string.IsNullOrWhiteSpace(instance.Name)
                    ? functionName
                    : instance.Name,
                BaseUrl = settings.BaseUrl,
                IndexUrl = settings.IndexUrl,
                MaxResults = settings.MaxResults,
            };

            var options = services.GetService<IOptions<DocumentationSearchOptions>>()?.Value ?? new DocumentationSearchOptions();
            var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
            var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger<SearchIndexDocumentationSource>()
                ?? NullLogger<SearchIndexDocumentationSource>.Instance;

            return new SearchIndexDocumentationSource(site, options, httpClientFactory, timeProvider, logger);
        });
    }
}
