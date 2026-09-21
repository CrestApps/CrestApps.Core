using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// A built-in <see cref="IDocumentationSource"/> that indexes a documentation site by downloading a
/// prebuilt search index published as JSON, then ranks its entries against a query using lightweight
/// keyword scoring. The downloaded corpus is cached in memory and refreshed based on
/// <see cref="DocumentationSearchOptions.CacheDuration"/>.
/// </summary>
/// <remarks>
/// "A prebuilt JSON search index" is not one format. The published shape is detected rather than
/// configured, so a site can be pointed at without anyone having to know what its generator emits. See
/// <see cref="ReadEntries"/> for the shapes understood and how they are told apart.
/// </remarks>
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
            var client = _httpClientFactory.CreateClient(DocumentationToolConstants.HttpClientName);

            using var stream = await client.GetStreamAsync(indexUrl, cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var entries = ReadEntries(document.RootElement, indexUrl);

            // Every way this can go wrong ends in an empty corpus, and an empty corpus reads to the caller as
            // "your query matched nothing". Each one is logged with the URL it came from, or a misconfigured
            // index is indistinguishable from a site that genuinely has no answer.
            if (entries is null)
            {
                _logger.LogWarning(
                    "Search index '{IndexUrl}' for documentation source '{SourceName}' is not in a recognized format. " +
                    "Expected a MkDocs index carrying a 'docs' array, or a Lunr/Docusaurus index that is an array of " +
                    "blocks carrying 'documents'.",
                    indexUrl,
                    _site.Name);

                return new DocumentationCorpus([]);
            }

            if (entries.Count == 0)
            {
                _logger.LogWarning(
                    "Search index '{IndexUrl}' for documentation source '{SourceName}' was read, but no entry carried " +
                    "both a location and text.",
                    indexUrl,
                    _site.Name);
            }

            return new DocumentationCorpus(entries);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read search index '{IndexUrl}' for documentation source '{SourceName}'.", indexUrl, _site.Name);

            return new DocumentationCorpus([]);
        }
    }

    /// <summary>
    /// Reads whichever published index shape this payload is.
    /// </summary>
    /// <param name="root">The parsed index.</param>
    /// <param name="indexUrl">The URL the index came from, used for logging.</param>
    /// <returns>The entries, or <see langword="null"/> when the shape is not one of the known ones.</returns>
    /// <remarks>
    /// The two known generators are told apart by their root and nothing else: MkDocs publishes an object
    /// carrying <c>docs</c>, and Docusaurus/Lunr an array of blocks each carrying <c>documents</c>. They are
    /// disjoint, so the format is detected instead of asked for, and an index that already worked keeps
    /// working with nothing to configure. Anything else is reported rather than guessed at.
    /// </remarks>
    private List<DocumentationCorpus.Entry> ReadEntries(JsonElement root, string indexUrl)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return ReadLunrIndex(root, indexUrl);
        }

        if (root.ValueKind == JsonValueKind.Object &&
            TryGetProperty(root, "docs", out var docs) &&
            docs.ValueKind == JsonValueKind.Array)
        {
            return ReadMkDocsIndex(docs);
        }

        return null;
    }

    /// <summary>
    /// Reads a MkDocs index, whose entries are one page each and carry their full text.
    /// </summary>
    /// <param name="docs">The <c>docs</c> array.</param>
    /// <returns>The entries.</returns>
    private List<DocumentationCorpus.Entry> ReadMkDocsIndex(JsonElement docs)
    {
        var entries = new List<DocumentationCorpus.Entry>(docs.GetArrayLength());

        foreach (var doc in docs.EnumerateArray())
        {
            if (doc.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var location = ReadString(doc, "location");
            var text = ReadString(doc, "text");

            if (string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            entries.Add(new DocumentationCorpus.Entry(ResolveUrl(location), ReadString(doc, "title"), text));
        }

        return entries;
    }

    /// <summary>
    /// Reads a Lunr/Docusaurus index.
    /// </summary>
    /// <param name="blocks">The array of index blocks.</param>
    /// <param name="indexUrl">The URL the index came from, used for logging.</param>
    /// <returns>The entries.</returns>
    /// <remarks>
    /// A block holds one kind of entry, and the generator emits several: page titles, headings, and the body
    /// split into passages. All of them use <c>t</c>, so for a passage that field is the prose rather than a
    /// name. The breadcrumbs say which page a passage belongs to, so the last one is the better title and
    /// <c>t</c> is the text.
    /// </remarks>
    private List<DocumentationCorpus.Entry> ReadLunrIndex(JsonElement blocks, string indexUrl)
    {
        var entries = new List<DocumentationCorpus.Entry>();

        foreach (var block in blocks.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(block, "documents", out var documents) ||
                documents.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var doc in documents.EnumerateArray())
            {
                if (doc.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var url = ReadString(doc, "u");
                var text = ReadString(doc, "t");

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var trail = new List<string>();

                if (TryGetProperty(doc, "b", out var breadcrumbs) && breadcrumbs.ValueKind == JsonValueKind.Array)
                {
                    trail.AddRange(breadcrumbs.EnumerateArray()
                        .Where(crumb => crumb.ValueKind == JsonValueKind.String)
                        .Select(crumb => crumb.GetString())
                        .Where(crumb => !string.IsNullOrWhiteSpace(crumb)));
                }

                // A page-title entry names itself in t and carries breadcrumbs; a passage entry puts the prose
                // in t and names its page in s. Taking s first, then the last breadcrumb, keeps the title a
                // name in both cases rather than a paragraph.
                var section = ReadString(doc, "s");
                var title = !string.IsNullOrWhiteSpace(section)
                    ? section
                    : trail.Count > 0 ? trail[^1] : text;

                // A passage entry also carries the heading anchor it sits under, which turns the citation into
                // a link to the passage rather than to the top of the page.
                var anchor = ReadString(doc, "h");
                var location = string.IsNullOrWhiteSpace(anchor)
                    ? url
                    : $"{url}{(anchor.StartsWith('#') ? anchor : "#" + anchor)}";

                var searchable = trail
                    .Append(section)
                    .Append(text)
                    .Where(part => !string.IsNullOrWhiteSpace(part));

                entries.Add(new DocumentationCorpus.Entry(ResolveUrl(location), title, string.Join(" ", searchable)));
            }
        }

        if (entries.Count > 0 && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Search index '{IndexUrl}' for documentation source '{SourceName}' is a Lunr/Docusaurus index, which " +
                "carries no page text; {EntryCount} entries were built from titles and breadcrumbs only.",
                indexUrl,
                _site.Name,
                entries.Count);
        }

        return entries;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;

                return true;
            }
        }

        value = default;

        return false;
    }

    private static string ReadString(JsonElement element, string name)
        => TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
        // Only an http(s) URL is already absolute. Asking Uri.TryCreate alone is not enough: on Unix a rooted
        // path parses as an absolute file URI, so "/docs/ai/" would be handed back unchanged there while being
        // resolved against the base URL on Windows. MkDocs locations are relative and never showed it; a
        // Lunr index states them rooted, so the same index resolved differently per platform.
        if (Uri.TryCreate(location, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return location;
        }

        return $"{_site.BaseUrl.TrimEnd('/')}/{location.TrimStart('/')}";
    }
}
