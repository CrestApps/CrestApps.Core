using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// A built-in <see cref="IDocumentationSource"/> that indexes a documentation site by downloading a
/// prebuilt search index published as JSON (for example a MkDocs Material <c>search_index.json</c>),
/// then ranks its entries against a query using lightweight keyword scoring. The downloaded corpus is
/// cached in memory and refreshed based on <see cref="DocumentationSearchOptions.CacheDuration"/>.
/// </summary>
public sealed class SearchIndexDocumentationSource : CachingDocumentationSource
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

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
        : base(site.Name, options.CacheDuration, timeProvider, options.FirstSearchWaitBudget)
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
            var client = _httpClientFactory.CreateClient(DocumentationToolConstants.HttpClientName);
            var index = await client.GetFromJsonAsync<SearchIndexDocument>(indexUrl, _serializerOptions, cancellationToken);

            // Every way this can go wrong ends in an empty corpus, and an empty corpus reads to the caller as
            // "your query matched nothing". Each one is logged with the URL it came from, or a misconfigured
            // index is indistinguishable from a site that genuinely has no answer.
            if (index is null)
            {
                _logger.LogWarning(
                    "Search index '{IndexUrl}' for documentation source '{SourceName}' returned no JSON body.",
                    indexUrl,
                    _site.Name);

                return new DocumentationCorpus([]);
            }

            if (index.Docs is null)
            {
                _logger.LogWarning(
                    "Search index '{IndexUrl}' for documentation source '{SourceName}' has no 'docs' array, so it is " +
                    "not a MkDocs-style search index. Nothing can be read from it.",
                    indexUrl,
                    _site.Name);

                return new DocumentationCorpus([]);
            }

            if (index.Docs.Count == 0)
            {
                _logger.LogWarning(
                    "Search index '{IndexUrl}' for documentation source '{SourceName}' contains no documents.",
                    indexUrl,
                    _site.Name);

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

            // A shape that parses but carries none of the fields searched on is the same dead end as an empty
            // index, and is what an index built for a different tool usually looks like.
            if (entries.Count == 0)
            {
                _logger.LogWarning(
                    "Search index '{IndexUrl}' for documentation source '{SourceName}' has {DocumentCount} document(s), " +
                    "but none carry both a location and text.",
                    indexUrl,
                    _site.Name,
                    index.Docs.Count);
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
        public IReadOnlyList<SearchIndexEntry> Docs { get; set; }
    }

    private sealed class SearchIndexEntry
    {
        public string Location { get; set; }

        public string Title { get; set; }

        public string Text { get; set; }
    }
}
