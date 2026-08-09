using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// An <see cref="IDocumentationSourceFactory"/> that materializes a stored entry into an
/// <see cref="AlgoliaDocumentationSource"/>.
/// </summary>
public sealed class AlgoliaDocumentationSourceFactory : IDocumentationSourceFactory
{
    private readonly DocumentationSearchOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlgoliaDocumentationSourceFactory"/> class.
    /// </summary>
    /// <param name="options">The documentation search options.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public AlgoliaDocumentationSourceFactory(
        IOptions<DocumentationSearchOptions> options,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public string Strategy => DocumentationSourceStrategies.Algolia;

    /// <inheritdoc />
    public IDocumentationSource Create(DocumentationSourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var site = new AlgoliaDocSearchSite
        {
            Name = entry.Name,
            ApplicationId = entry.ApplicationId,
            ApiKey = entry.ApiKey,
            IndexName = entry.IndexName,
            MaxResults = entry.MaxResults,
        };

        return new AlgoliaDocumentationSource(
            site,
            _options,
            _httpClientFactory,
            _loggerFactory.CreateLogger<AlgoliaDocumentationSource>());
    }
}
