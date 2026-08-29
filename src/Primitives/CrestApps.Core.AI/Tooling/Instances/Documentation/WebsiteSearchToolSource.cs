using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// The built-in <see cref="IAIToolInstanceSource"/> that lets users configure a live website search over a
/// single site's own search API (for example the WordPress REST <c>wp-json/wp/v2/search</c> endpoint).
/// Each configured <see cref="AIToolInstance"/> binds one site; the AI model only supplies the search
/// query. Unlike the sitemap source, this issues a live query per search and does not crawl or cache.
/// </summary>
public sealed class WebsiteSearchToolSource : IAIToolInstanceSource
{
    /// <summary>
    /// Creates the <see cref="DocumentationSearchToolFunction"/> bound to the supplied instance's settings.
    /// </summary>
    /// <param name="instance">The configured tool instance whose settings should be bound to the produced tool.</param>
    /// <returns>The configured documentation search function.</returns>
    public AITool CreateTool(AIToolInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var settings = instance.GetOrCreate<WebsiteSearchToolSettings>();

        var functionName = instance.GetFunctionName();
        var description = string.IsNullOrWhiteSpace(instance.Description)
            ? "Searches the configured website and returns the most relevant pages with their titles, source URLs, and text snippets."
            : instance.Description;

        return new DocumentationSearchToolFunction(functionName, description, instance, services =>
        {
            var site = new WebsiteSearchSite
            {
                Name = string.IsNullOrWhiteSpace(instance.Name)
                    ? functionName
                    : instance.Name,
                BaseUrl = settings.BaseUrl,
                SearchPath = settings.SearchPath,
                QueryParameter = settings.QueryParameter,
                ExtraQuery = settings.ExtraQuery,
                ResultsPath = settings.ResultsPath,
                TitlePath = settings.TitlePath,
                UrlPath = settings.UrlPath,
                SnippetPath = settings.SnippetPath,
                MaxResults = settings.MaxResults,
            };

            var options = services.GetService<IOptions<DocumentationSearchOptions>>()?.Value ?? new DocumentationSearchOptions();
            var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger<WebsiteSearchSource>()
                ?? NullLogger<WebsiteSearchSource>.Instance;

            return new WebsiteSearchSource(site, options, httpClientFactory, logger);
        });
    }
}
