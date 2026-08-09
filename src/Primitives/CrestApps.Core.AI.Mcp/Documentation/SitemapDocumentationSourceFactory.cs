using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// An <see cref="IDocumentationSourceFactory"/> that materializes a stored entry into a
/// <see cref="SitemapDocumentationSource"/>.
/// </summary>
public sealed class SitemapDocumentationSourceFactory : IDocumentationSourceFactory
{
    private readonly DocumentationSearchOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SitemapDocumentationSourceFactory"/> class.
    /// </summary>
    /// <param name="options">The documentation search options.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public SitemapDocumentationSourceFactory(
        IOptions<DocumentationSearchOptions> options,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public string Strategy => DocumentationSourceStrategies.Sitemap;

    /// <inheritdoc />
    public IDocumentationSource Create(DocumentationSourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var site = new DocumentationSite
        {
            Name = entry.Name,
            BaseUrl = entry.BaseUrl,
            SitemapUrl = entry.SitemapUrl,
            MaxResults = entry.MaxResults,
            MaxPages = entry.MaxPages,
        };

        return new SitemapDocumentationSource(
            site,
            _options,
            _httpClientFactory,
            _timeProvider,
            _loggerFactory.CreateLogger<SitemapDocumentationSource>());
    }
}
