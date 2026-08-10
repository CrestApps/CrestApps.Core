using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// A built-in <see cref="IDocumentationSource"/> that searches a documentation site through the
/// Algolia DocSearch query API. Queries are forwarded to Algolia, which performs the ranking, and the
/// returned hits are mapped to documentation results. This source does not crawl or cache a corpus
/// locally; each search issues a live query.
/// </summary>
public sealed class AlgoliaDocumentationSource : IDocumentationSource
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AlgoliaDocSearchSite _site;
    private readonly DocumentationSearchOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlgoliaDocumentationSource"/> class.
    /// </summary>
    /// <param name="site">The site configuration.</param>
    /// <param name="options">The global documentation search options.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public AlgoliaDocumentationSource(
        AlgoliaDocSearchSite site,
        DocumentationSearchOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger logger)
    {
        _site = site;
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => _site.Name;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentationSearchResult>> SearchAsync(DocumentationSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return [];
        }

        var maxResults = Math.Min(request.MaxResults, _site.MaxResults ?? _options.MaxResultsPerSite);

        if (maxResults <= 0)
        {
            return [];
        }

        try
        {
            var hits = await QueryAsync(request.Query, maxResults, cancellationToken);

            if (hits.Count == 0)
            {
                return [];
            }

            var results = new List<DocumentationSearchResult>(hits.Count);

            for (var i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];

                if (string.IsNullOrWhiteSpace(hit.Url))
                {
                    continue;
                }

                results.Add(new DocumentationSearchResult
                {
                    SourceName = Name,
                    Title = ResolveTitle(hit),
                    Url = hit.Url,
                    Snippet = hit.Content,
                    Score = hits.Count - i,
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Algolia DocSearch for documentation source '{SourceName}'.", _site.Name);

            return [];
        }
    }

    private async Task<IReadOnlyList<AlgoliaHit>> QueryAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(DocumentationToolConstants.HttpClientName);

        var requestUri = $"https://{_site.ApplicationId}-dsn.algolia.net/1/indexes/{Uri.EscapeDataString(_site.IndexName)}/query";
        var parameters = $"query={Uri.EscapeDataString(query)}&hitsPerPage={maxResults}";

        using var message = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new AlgoliaQueryRequest { Params = parameters }, options: _serializerOptions),
        };

        message.Headers.TryAddWithoutValidation("X-Algolia-Application-Id", _site.ApplicationId);
        message.Headers.TryAddWithoutValidation("X-Algolia-API-Key", _site.ApiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(message, cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<AlgoliaQueryResponse>(_serializerOptions, cancellationToken);

        return payload?.Hits ?? [];
    }

    private static string ResolveTitle(AlgoliaHit hit)
    {
        if (hit.Hierarchy is not null)
        {
            foreach (var level in new[] { hit.Hierarchy.Lvl6, hit.Hierarchy.Lvl5, hit.Hierarchy.Lvl4, hit.Hierarchy.Lvl3, hit.Hierarchy.Lvl2, hit.Hierarchy.Lvl1, hit.Hierarchy.Lvl0 })
            {
                if (!string.IsNullOrWhiteSpace(level))
                {
                    return level;
                }
            }
        }

        return hit.Url;
    }

    private sealed class AlgoliaQueryRequest
    {
        public string Params { get; set; }
    }

    private sealed class AlgoliaQueryResponse
    {
        public IReadOnlyList<AlgoliaHit> Hits { get; set; }
    }

    private sealed class AlgoliaHit
    {
        public string Url { get; set; }

        public string Content { get; set; }

        public AlgoliaHierarchy Hierarchy { get; set; }
    }

    private sealed class AlgoliaHierarchy
    {
        public string Lvl0 { get; set; }

        public string Lvl1 { get; set; }

        public string Lvl2 { get; set; }

        public string Lvl3 { get; set; }

        public string Lvl4 { get; set; }

        public string Lvl5 { get; set; }

        public string Lvl6 { get; set; }
    }
}
