using System.Net;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// Convenience registration for the built-in documentation search tool instance sources. Each source is a
/// developer-authored blueprint that users configure one or more times as <see cref="Tooling.AIToolInstance"/>
/// catalog entries, producing one callable documentation search function per configured site. The sources
/// are registered on the tool instances builder so they can be persisted and managed through a store or UI.
/// </summary>
public static class DocumentationToolInstanceServiceCollectionExtensions
{
    /// <summary>
    /// Registers all built-in documentation search sources (sitemap crawling, prebuilt JSON search index,
    /// and Algolia DocSearch) on the tool instances builder.
    /// </summary>
    /// <param name="builder">The tool instances builder.</param>
    /// <returns>The tool instances builder, for chaining.</returns>
    public static CrestAppsAIToolInstancesBuilder AddDocumentationSearchSources(this CrestAppsAIToolInstancesBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .AddSitemapDocumentationSource()
            .AddSearchIndexDocumentationSource()
            .AddAlgoliaDocumentationSource();

        return builder;
    }

    /// <summary>
    /// Registers the sitemap crawling documentation search source on the tool instances builder so users
    /// can create configured instances that search a public site through its <c>sitemap.xml</c>.
    /// </summary>
    /// <param name="builder">The tool instances builder.</param>
    /// <param name="configure">An optional delegate used to override the source display metadata.</param>
    /// <returns>The tool instances builder, for chaining.</returns>
    public static CrestAppsAIToolInstancesBuilder AddSitemapDocumentationSource(
        this CrestAppsAIToolInstancesBuilder builder,
        Action<AIToolInstanceSourceEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        AddSharedServices(builder);

        builder.AddSource<SitemapDocumentationToolSource>(DocumentationToolConstants.SitemapSourceName, entry =>
        {
            entry.DisplayName = new LocalizedString(DocumentationToolConstants.SitemapSourceName, "Documentation search (sitemap)");
            entry.Description = new LocalizedString(
                DocumentationToolConstants.SitemapSourceName,
                "Searches a documentation site by crawling its sitemap.xml (for example a public Docusaurus site).");
            entry.Category = new LocalizedString(DocumentationToolConstants.Category, DocumentationToolConstants.Category);

            configure?.Invoke(entry);
        });

        return builder;
    }

    /// <summary>
    /// Registers the prebuilt JSON search index documentation search source on the tool instances builder
    /// so users can create configured instances that search a site publishing a <c>search_index.json</c>.
    /// </summary>
    /// <param name="builder">The tool instances builder.</param>
    /// <param name="configure">An optional delegate used to override the source display metadata.</param>
    /// <returns>The tool instances builder, for chaining.</returns>
    public static CrestAppsAIToolInstancesBuilder AddSearchIndexDocumentationSource(
        this CrestAppsAIToolInstancesBuilder builder,
        Action<AIToolInstanceSourceEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        AddSharedServices(builder);

        builder.AddSource<SearchIndexDocumentationToolSource>(DocumentationToolConstants.SearchIndexSourceName, entry =>
        {
            entry.DisplayName = new LocalizedString(DocumentationToolConstants.SearchIndexSourceName, "Documentation search (search index)");
            entry.Description = new LocalizedString(
                DocumentationToolConstants.SearchIndexSourceName,
                "Searches a documentation site that publishes a prebuilt JSON search index (for example MkDocs Material).");
            entry.Category = new LocalizedString(DocumentationToolConstants.Category, DocumentationToolConstants.Category);

            configure?.Invoke(entry);
        });

        return builder;
    }

    /// <summary>
    /// Registers the Algolia DocSearch documentation search source on the tool instances builder so users
    /// can create configured instances that query the hosted Algolia DocSearch API.
    /// </summary>
    /// <param name="builder">The tool instances builder.</param>
    /// <param name="configure">An optional delegate used to override the source display metadata.</param>
    /// <returns>The tool instances builder, for chaining.</returns>
    public static CrestAppsAIToolInstancesBuilder AddAlgoliaDocumentationSource(
        this CrestAppsAIToolInstancesBuilder builder,
        Action<AIToolInstanceSourceEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        AddSharedServices(builder);

        builder.AddSource<AlgoliaDocumentationToolSource>(DocumentationToolConstants.AlgoliaSourceName, entry =>
        {
            entry.DisplayName = new LocalizedString(DocumentationToolConstants.AlgoliaSourceName, "Documentation search (Algolia DocSearch)");
            entry.Description = new LocalizedString(
                DocumentationToolConstants.AlgoliaSourceName,
                "Searches a documentation site through the hosted Algolia DocSearch query API.");
            entry.Category = new LocalizedString(DocumentationToolConstants.Category, DocumentationToolConstants.Category);

            configure?.Invoke(entry);
        });

        return builder;
    }

    private static void AddSharedServices(CrestAppsAIToolInstancesBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IDocumentationSourceMaterializer, DefaultDocumentationSourceMaterializer>();

        builder.Services
            .AddHttpClient(DocumentationToolConstants.HttpClientName, client =>
            {
                // Many hosts (Cloudflare and other WAFs) reject requests without a User-Agent, so the
                // crawler always presents an identifiable one. A 30 second timeout keeps a slow or
                // unresponsive site from stalling a search indefinitely.
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CrestApps-DocumentationBot/1.0 (+https://crestapps.com)");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // Transparently handle gzip/deflate/brotli responses (common for sitemaps and pages) and
                // follow the redirects that sites such as Yoast/Rank Math use for /sitemap.xml.
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                AllowAutoRedirect = true,
            });
    }
}
