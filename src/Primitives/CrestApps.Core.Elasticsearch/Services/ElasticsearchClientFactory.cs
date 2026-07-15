using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace CrestApps.Core.Elasticsearch.Services;

/// <summary>
/// Creates Elasticsearch clients from the configured connection options.
/// </summary>
public sealed class ElasticsearchClientFactory : IElasticsearchClientFactory
{
    private static readonly ResiliencePipeline<HttpResponseMessage> HttpResiliencePipeline = CreateHttpResiliencePipeline();
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

        var authenticationType = configuration.GetAuthenticationType();
        var hasCloudId = !string.IsNullOrWhiteSpace(configuration.CloudId);
        var hasUrl = !string.IsNullOrWhiteSpace(configuration.Url);

        if (!hasCloudId && !hasUrl)
        {
            throw new InvalidOperationException("Elasticsearch is not configured. Set either the URL or the Cloud ID.");
        }

        AuthorizationHeader authorizationHeader = null;
        if (!string.Equals(authenticationType, ElasticsearchConnectionOptions.NoneAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            authorizationHeader = CreateAuthorizationHeader(configuration, authenticationType);
        }

        if (hasCloudId && authorizationHeader == null)
        {
            throw new InvalidOperationException("Elastic Cloud connections require an authentication type and matching credentials.");
        }

        ElasticsearchClientSettings settings;
        object connectionTarget;

        if (hasCloudId)
        {
            settings = new ElasticsearchClientSettings(
                new CloudNodePool(configuration.CloudId.Trim(), authorizationHeader ?? throw new InvalidOperationException("Elastic Cloud connections require an authentication type and matching credentials.")),
                CreateRequestInvoker());
            connectionTarget = "Elastic Cloud";
        }
        else
        {
            if (!Uri.TryCreate(configuration.Url.Trim(), UriKind.Absolute, out var endpoint))
            {
                throw new InvalidOperationException("The Elasticsearch URL is invalid.");
            }

            settings = new ElasticsearchClientSettings(new SingleNodePool(endpoint), CreateRequestInvoker());
            connectionTarget = endpoint;
        }

        if (authorizationHeader != null)
        {
            settings.Authentication(authorizationHeader);
        }

        if (!string.IsNullOrWhiteSpace(configuration.CertificateFingerprint))
        {
            settings.CertificateFingerprint(configuration.CertificateFingerprint.Trim());
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Creating Elasticsearch client for target '{Target}' with authentication type '{AuthenticationType}'.",
                connectionTarget,
                authenticationType);
        }

        return new ElasticsearchClient(settings);
    }

    internal static AuthorizationHeader CreateAuthorizationHeader(ElasticsearchConnectionOptions configuration, string authenticationType)
    {
        if (string.Equals(authenticationType, ElasticsearchConnectionOptions.BasicAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            var hasUsername = !string.IsNullOrWhiteSpace(configuration.Username);
            var hasPassword = !string.IsNullOrWhiteSpace(configuration.Password);

            if (hasUsername != hasPassword)
            {
                throw new InvalidOperationException("Elasticsearch basic authentication requires both username and password.");
            }

            return hasUsername
                ? new BasicAuthentication(configuration.Username, configuration.Password)
                : throw new InvalidOperationException("Elasticsearch basic authentication requires both username and password.");
        }

        if (string.Equals(authenticationType, ElasticsearchConnectionOptions.ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(configuration.ApiKey)
                ? new ApiKey(configuration.ApiKey)
                : throw new InvalidOperationException("Elasticsearch API key authentication requires an API key.");
        }

        if (string.Equals(authenticationType, ElasticsearchConnectionOptions.Base64ApiKeyAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(configuration.Base64ApiKey)
                ? new Base64ApiKey(configuration.Base64ApiKey)
                : throw new InvalidOperationException("Elasticsearch base64 API key authentication requires a base64-encoded API key.");
        }

        if (string.Equals(authenticationType, ElasticsearchConnectionOptions.KeyIdAndKeyAuthenticationType, StringComparison.OrdinalIgnoreCase))
        {
            var hasApiKeyId = !string.IsNullOrWhiteSpace(configuration.ApiKeyId);
            var hasApiKey = !string.IsNullOrWhiteSpace(configuration.ApiKey);

            if (!hasApiKeyId || !hasApiKey)
            {
                throw new InvalidOperationException("Elasticsearch key ID and key authentication requires both an API key ID and API key.");
            }

            return new Base64ApiKey(configuration.ApiKeyId, configuration.ApiKey);
        }

        throw new InvalidOperationException($"Unsupported Elasticsearch authentication type '{authenticationType}'.");
    }

    internal static IRequestInvoker CreateRequestInvoker()
    {
        return new HttpRequestInvoker(CreateResilientHttpMessageHandler);
    }

    internal static HttpMessageHandler CreateResilientHttpMessageHandler(HttpMessageHandler innerHandler, BoundConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(innerHandler);
        ArgumentNullException.ThrowIfNull(configuration);

        return new ResilienceHandler(HttpResiliencePipeline)
        {
            InnerHandler = innerHandler,
        };
    }

    private static ResiliencePipeline<HttpResponseMessage> CreateHttpResiliencePipeline()
    {
        var options = new HttpStandardResilienceOptions();

        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRateLimiter(options.RateLimiter)
            .AddTimeout(options.TotalRequestTimeout)
            .AddRetry(options.Retry)
            .AddCircuitBreaker(options.CircuitBreaker)
            .AddTimeout(options.AttemptTimeout)
            .Build();
    }
}
