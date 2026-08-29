using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// A built-in <see cref="IDocumentationSource"/> that searches a site through its own search API (for
/// example the WordPress REST <c>wp-json/wp/v2/search</c> endpoint) and maps the JSON response to
/// documentation results. This source issues a live query per request — it does not crawl the site or
/// cache a corpus locally, so there is no cold-start indexing delay and results reflect the site's own
/// relevance ranking. The response shape is configurable through dotted field paths, with defaults that
/// match WordPress.
/// </summary>
public sealed partial class WebsiteSearchSource : IDocumentationSource
{
    private readonly WebsiteSearchSite _site;
    private readonly DocumentationSearchOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebsiteSearchSource"/> class.
    /// </summary>
    /// <param name="site">The site configuration.</param>
    /// <param name="options">The global documentation search options.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public WebsiteSearchSource(
        WebsiteSearchSite site,
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

        if (string.IsNullOrWhiteSpace(request.Query) || string.IsNullOrWhiteSpace(_site.BaseUrl))
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
            var client = _httpClientFactory.CreateClient(DocumentationToolConstants.HttpClientName);
            var requestUri = BuildRequestUri(request.Query);

            using var response = await client.GetAsync(requestUri, cancellationToken);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return MapResults(document.RootElement, maxResults);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query the website search API for documentation source '{SourceName}'.", _site.Name);

            return [];
        }
    }

    private string BuildRequestUri(string query)
    {
        var endpoint = $"{_site.BaseUrl.TrimEnd('/')}{_site.SearchPath}";
        var parameter = string.IsNullOrWhiteSpace(_site.QueryParameter) ? "search" : _site.QueryParameter;
        var queryString = $"{Uri.EscapeDataString(parameter)}={Uri.EscapeDataString(query)}";

        if (!string.IsNullOrWhiteSpace(_site.ExtraQuery))
        {
            queryString += "&" + _site.ExtraQuery.TrimStart('&', '?');
        }

        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";

        return endpoint + separator + queryString;
    }

    private List<DocumentationSearchResult> MapResults(JsonElement root, int maxResults)
    {
        if (!TryResolve(root, _site.ResultsPath, out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var length = results.GetArrayLength();

        if (length == 0)
        {
            return [];
        }

        var mapped = new List<DocumentationSearchResult>(Math.Min(length, maxResults));
        var index = 0;

        foreach (var item in results.EnumerateArray())
        {
            if (mapped.Count >= maxResults)
            {
                break;
            }

            var url = GetStringAt(item, _site.UrlPath);

            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var title = GetStringAt(item, _site.TitlePath);
            var snippet = StripHtml(GetStringAt(item, _site.SnippetPath));

            mapped.Add(new DocumentationSearchResult
            {
                SourceName = Name,
                Title = string.IsNullOrWhiteSpace(title) ? url : title,
                Url = url,
                Snippet = snippet,

                // The site returns results already ranked by relevance, so preserve that order.
                Score = length - index,
            });

            index++;
        }

        return mapped;
    }

    /// <summary>
    /// Reads a string value at <paramref name="path"/> relative to <paramref name="element"/>. Resolves a
    /// JSON string directly, or an object's <c>rendered</c> string (the WordPress rendered-field shape).
    /// </summary>
    private static string GetStringAt(JsonElement element, string path)
    {
        if (!TryResolve(element, path, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object when value.TryGetProperty("rendered", out var rendered) && rendered.ValueKind == JsonValueKind.String
                => rendered.GetString(),
            _ => null,
        };
    }

    /// <summary>
    /// Resolves a dotted path (supporting property names and single array indices such as
    /// <c>_embedded.self[0].excerpt.rendered</c>) from <paramref name="current"/>. An empty path returns
    /// the element itself.
    /// </summary>
    private static bool TryResolve(JsonElement current, string path, out JsonElement value)
    {
        value = current;

        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = segment;
            int? index = null;
            var open = segment.IndexOf('[', StringComparison.Ordinal);

            if (open >= 0 && segment.EndsWith(']'))
            {
                if (int.TryParse(segment[(open + 1)..^1], out var parsed))
                {
                    index = parsed;
                }

                name = segment[..open];
            }

            if (!string.IsNullOrEmpty(name))
            {
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out value))
                {
                    value = default;

                    return false;
                }
            }

            if (index is int i)
            {
                if (value.ValueKind != JsonValueKind.Array || i < 0 || i >= value.GetArrayLength())
                {
                    value = default;

                    return false;
                }

                value = value[i];
            }
        }

        return true;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var withoutTags = TagRegex().Replace(html, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        var collapsed = WhitespaceRegex().Replace(decoded, " ").Trim();

        return string.IsNullOrEmpty(collapsed) ? null : collapsed;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
