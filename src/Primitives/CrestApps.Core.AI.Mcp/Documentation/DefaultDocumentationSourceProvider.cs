using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Default <see cref="IDocumentationSourceProvider"/> that combines documentation sources registered
/// in code with built-in crawler sources materialized from <see cref="DocumentationSearchOptions.Sites"/>.
/// Crawler sources are created once per configured site and reused so their in-memory corpus is cached.
/// </summary>
public sealed class DefaultDocumentationSourceProvider : IDocumentationSourceProvider
{
    private readonly IEnumerable<IDocumentationSource> _customSources;
    private readonly IOptions<DocumentationSearchOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, SitemapDocumentationSource> _siteSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SearchIndexDocumentationSource> _searchIndexSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AlgoliaDocumentationSource> _algoliaSources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDocumentationSourceProvider"/> class.
    /// </summary>
    /// <param name="customSources">The documentation sources registered in code.</param>
    /// <param name="options">The documentation search options.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="timeProvider">The time provider.</param>
    public DefaultDocumentationSourceProvider(
        IEnumerable<IDocumentationSource> customSources,
        IOptions<DocumentationSearchOptions> options,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        _customSources = customSources;
        _options = options;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public IReadOnlyList<IDocumentationSource> GetSources()
    {
        var options = _options.Value;
        var sources = new List<IDocumentationSource>(_customSources);

        foreach (var site in options.Sites)
        {
            if (string.IsNullOrWhiteSpace(site.Name) || string.IsNullOrWhiteSpace(site.BaseUrl))
            {
                continue;
            }

            var source = _siteSources.GetOrAdd(site.Name, _ => new SitemapDocumentationSource(
                site,
                options,
                _httpClientFactory,
                _timeProvider,
                _loggerFactory.CreateLogger<SitemapDocumentationSource>()));

            sources.Add(source);
        }

        foreach (var site in options.SearchIndexes)
        {
            if (string.IsNullOrWhiteSpace(site.Name) || string.IsNullOrWhiteSpace(site.BaseUrl))
            {
                continue;
            }

            var source = _searchIndexSources.GetOrAdd(site.Name, _ => new SearchIndexDocumentationSource(
                site,
                options,
                _httpClientFactory,
                _timeProvider,
                _loggerFactory.CreateLogger<SearchIndexDocumentationSource>()));

            sources.Add(source);
        }

        foreach (var site in options.AlgoliaSources)
        {
            if (string.IsNullOrWhiteSpace(site.Name)
                || string.IsNullOrWhiteSpace(site.ApplicationId)
                || string.IsNullOrWhiteSpace(site.ApiKey)
                || string.IsNullOrWhiteSpace(site.IndexName))
            {
                continue;
            }

            var source = _algoliaSources.GetOrAdd(site.Name, _ => new AlgoliaDocumentationSource(
                site,
                options,
                _httpClientFactory,
                _loggerFactory.CreateLogger<AlgoliaDocumentationSource>()));

            sources.Add(source);
        }

        return sources;
    }
}
