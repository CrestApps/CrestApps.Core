using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// A built-in <see cref="IDocumentationSource"/> that searches a documentation site through the
/// Algolia DocSearch query API. Queries are forwarded to Algolia, which performs the ranking, and the
/// returned hits are mapped to documentation results. This source does not crawl or cache a corpus
/// locally; each search issues a live query.
/// </summary>
public sealed class AlgoliaDocumentationSource : IDocumentationSource
{
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
        var client = _httpClientFactory.CreateClient(McpConstants.DocumentationHttpClientName);

        var requestUri = $"https://{_site.ApplicationId}-dsn.algolia.net/1/indexes/{Uri.EscapeDataString(_site.IndexName)}/query";
        var parameters = $"query={Uri.EscapeDataString(query)}&hitsPerPage={maxResults}";

        using var message = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new AlgoliaQueryRequest { Params = parameters }),
        };

        message.Headers.TryAddWithoutValidation("X-Algolia-Application-Id", _site.ApplicationId);
        message.Headers.TryAddWithoutValidation("X-Algolia-API-Key", _site.ApiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(message, cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<AlgoliaQueryResponse>(cancellationToken);

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
        [JsonPropertyName("params")]
        public string Params { get; set; }
    }

    private sealed class AlgoliaQueryResponse
    {
        [JsonPropertyName("hits")]
        public IReadOnlyList<AlgoliaHit> Hits { get; set; }
    }

    private sealed class AlgoliaHit
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }

        [JsonPropertyName("hierarchy")]
        public AlgoliaHierarchy Hierarchy { get; set; }
    }

    private sealed class AlgoliaHierarchy
    {
        [JsonPropertyName("lvl0")]
        public string Lvl0 { get; set; }

        [JsonPropertyName("lvl1")]
        public string Lvl1 { get; set; }

        [JsonPropertyName("lvl2")]
        public string Lvl2 { get; set; }

        [JsonPropertyName("lvl3")]
        public string Lvl3 { get; set; }

        [JsonPropertyName("lvl4")]
        public string Lvl4 { get; set; }

        [JsonPropertyName("lvl5")]
        public string Lvl5 { get; set; }

        [JsonPropertyName("lvl6")]
        public string Lvl6 { get; set; }
    }
}
