using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// A built-in <see cref="IDocumentationSource"/> that indexes a documentation site by downloading a
/// prebuilt search index published as JSON (for example a MkDocs Material <c>search_index.json</c>),
/// then ranks its entries against a query using lightweight keyword scoring. The downloaded corpus is
/// cached in memory and refreshed based on <see cref="DocumentationSearchOptions.CacheDuration"/>.
/// </summary>
public sealed class SearchIndexDocumentationSource : CachingDocumentationSource
{
    private readonly DocumentationSearchIndexSite _site;
    private readonly DocumentationSearchOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchIndexDocumentationSource"/> class.
    /// </summary>
    /// <param name="site">The site configuration.</param>
    /// <param name="options">The global documentation search options.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public SearchIndexDocumentationSource(
        DocumentationSearchIndexSite site,
        DocumentationSearchOptions options,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger logger)
        : base(site.Name, options.CacheDuration, timeProvider)
    {
        _site = site;
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override int MaxResults => _site.MaxResults ?? _options.MaxResultsPerSite;

    /// <inheritdoc />
    protected override async Task<DocumentationCorpus> BuildCorpusAsync(CancellationToken cancellationToken)
    {
        var indexUrl = ResolveIndexUrl();

        try
        {
            var client = _httpClientFactory.CreateClient(McpConstants.DocumentationHttpClientName);
            var index = await client.GetFromJsonAsync<SearchIndexDocument>(indexUrl, cancellationToken);

            if (index?.Docs is null || index.Docs.Count == 0)
            {
                return new DocumentationCorpus([]);
            }

            var entries = new List<DocumentationCorpus.Entry>(index.Docs.Count);

            foreach (var doc in index.Docs)
            {
                if (string.IsNullOrWhiteSpace(doc.Location) || string.IsNullOrWhiteSpace(doc.Text))
                {
                    continue;
                }

                entries.Add(new DocumentationCorpus.Entry(ResolveUrl(doc.Location), doc.Title, doc.Text));
            }

            return new DocumentationCorpus(entries);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read search index '{IndexUrl}' for documentation source '{SourceName}'.", indexUrl, _site.Name);

            return new DocumentationCorpus([]);
        }
    }

    private string ResolveIndexUrl()
    {
        if (!string.IsNullOrWhiteSpace(_site.IndexUrl))
        {
            return _site.IndexUrl;
        }

        return $"{_site.BaseUrl.TrimEnd('/')}/search/search_index.json";
    }

    private string ResolveUrl(string location)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out _))
        {
            return location;
        }

        return $"{_site.BaseUrl.TrimEnd('/')}/{location.TrimStart('/')}";
    }

    private sealed class SearchIndexDocument
    {
        [JsonPropertyName("docs")]
        public IReadOnlyList<SearchIndexEntry> Docs { get; set; }
    }

    private sealed class SearchIndexEntry
    {
        [JsonPropertyName("location")]
        public string Location { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }
    }
}
