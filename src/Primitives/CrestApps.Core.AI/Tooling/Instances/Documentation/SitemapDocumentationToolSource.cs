using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// The built-in <see cref="IAIToolInstanceSource"/> that lets users configure documentation search over a
/// single site crawled through its <c>sitemap.xml</c> (for example a public Docusaurus site). Each
/// configured <see cref="AIToolInstance"/> binds one site; the AI model only supplies the search query.
/// </summary>
public sealed class SitemapDocumentationToolSource : IAIToolInstanceSource
{
    /// <summary>
    /// Creates the <see cref="DocumentationSearchToolFunction"/> bound to the supplied instance's settings.
    /// </summary>
    /// <param name="instance">The configured tool instance whose settings should be bound to the produced tool.</param>
    /// <returns>The configured documentation search function.</returns>
    public AITool CreateTool(AIToolInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var settings = instance.TryGet<SitemapDocumentationToolSettings>(out var stored)
            ? stored
            : new SitemapDocumentationToolSettings();

        var functionName = instance.GetFunctionName();
        var description = string.IsNullOrWhiteSpace(instance.Description)
            ? "Searches the configured documentation site and returns the most relevant passages with their source URLs."
            : instance.Description;

        return new DocumentationSearchToolFunction(functionName, description, instance, services =>
        {
            var site = new DocumentationSite
            {
                Name = string.IsNullOrWhiteSpace(instance.Name)
                    ? functionName
                    : instance.Name,
                BaseUrl = settings.BaseUrl,
                SitemapUrl = settings.SitemapUrl,
                MaxResults = settings.MaxResults,
                MaxPages = settings.MaxPages,
            };

            var options = services.GetService<IOptions<DocumentationSearchOptions>>()?.Value ?? new DocumentationSearchOptions();
            var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
            var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger<SitemapDocumentationSource>()
                ?? NullLogger<SitemapDocumentationSource>.Instance;

            return new SitemapDocumentationSource(site, options, httpClientFactory, timeProvider, logger);
        });
    }
}
