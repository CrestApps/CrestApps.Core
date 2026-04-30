using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Elasticsearch.Services;

/// <summary>
/// Creates Elasticsearch clients from the configured connection options.
/// </summary>
public sealed class ElasticsearchClientFactory : IElasticsearchClientFactory
{
    private readonly ILogger<ElasticsearchClientFactory> _logger;
    private readonly ElasticsearchConnectionOptions _options;
    private readonly object _syncLock = new();

    private ElasticsearchClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="ElasticsearchClientFactory"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="options">The options.</param>
    public ElasticsearchClientFactory(
        ILogger<ElasticsearchClientFactory> logger,
        IOptions<ElasticsearchConnectionOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Creates the operation.
    /// </summary>
    public ElasticsearchClient Create()
    {
        if (_client != null)
        {
            return _client;
        }

        lock (_syncLock)
        {
            _client ??= Create(_options);
        }

        return _client;
    }

    /// <summary>
    /// Creates the operation.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    public ElasticsearchClient Create(ElasticsearchConnectionOptions configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.Url))
        {
            throw new InvalidOperationException("Elasticsearch is not configured.");
        }

        if (!Uri.TryCreate(configuration.Url.Trim(), UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException("The Elasticsearch URL is invalid.");
        }

        var settings = new ElasticsearchClientSettings(endpoint);
        var hasUsername = !string.IsNullOrWhiteSpace(configuration.Username);
        var hasPassword = !string.IsNullOrWhiteSpace(configuration.Password);

        if (hasUsername != hasPassword)
        {
            throw new InvalidOperationException("Elasticsearch basic authentication requires both username and password.");
        }

        if (hasUsername)
        {
            settings.Authentication(new BasicAuthentication(configuration.Username, configuration.Password));
        }

        if (!string.IsNullOrWhiteSpace(configuration.CertificateFingerprint))
        {
            settings.CertificateFingerprint(configuration.CertificateFingerprint.Trim());
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Creating Elasticsearch client for endpoint '{Endpoint}' with authentication configured: {HasAuthentication}.",
                endpoint,
                hasUsername);
        }

        return new ElasticsearchClient(settings);
    }
}
