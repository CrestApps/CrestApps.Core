#pragma warning disable OPENAI001 // The AuthenticationPolicy constructors of the OpenAI SDK are for evaluation purposes only.
#pragma warning disable OPENAI002 // The OpenAI Realtime API is experimental and requires explicit opt-in at each usage site.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Azure.Core;
using Azure.Identity;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.Azure;
using CrestApps.Core.Azure.Models;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Realtime;

namespace CrestApps.Core.AI.OpenAI.Azure.Services;

/// <summary>
/// Builds the OpenAI SDK clients for an Azure OpenAI connection. Azure OpenAI serves the OpenAI API on the resource's
/// <c>/openai/v1/</c> endpoint, so the plain <c>OpenAI</c> SDK talks to it directly: the deployment name is sent as the
/// model and no <c>api-version</c> is needed.
/// </summary>
internal static class AzureOpenAIClientFactory
{
    /// <summary>
    /// The token scope used for Microsoft Entra ID authentication against Azure OpenAI.
    /// </summary>
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";

    /// <summary>
    /// The header that carries the API key on a realtime WebSocket handshake.
    /// </summary>
    private const string ApiKeyHeaderName = "api-key";

    private static readonly string[] _cognitiveServicesScopes = [CognitiveServicesScope];

    private static readonly ConcurrentDictionary<string, OpenAIClient> _clientCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Clears the cached Azure OpenAI client instances.
    /// </summary>
    public static void ClearCache() => _clientCache.Clear();

    /// <summary>
    /// Gets the v1 API endpoint of the connection's resource, <c>https://&lt;resource&gt;.openai.azure.com/openai/v1/</c>.
    /// </summary>
    /// <param name="connection">The connection.</param>
    public static Uri GetEndpoint(AIProviderConnectionEntry connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new Uri(connection.GetEndpoint(), "/openai/v1/");
    }

    /// <summary>
    /// Creates, or returns from the cache, the client that serves chat, Responses, embeddings, images and audio.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="options">The options.</param>
    public static OpenAIClient Create(
        AIProviderConnectionEntry connection,
        ILoggerFactory loggerFactory,
        AzureClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var endpoint = GetEndpoint(connection);
        var authType = connection.GetAzureAuthenticationType();
        var identityId = connection.GetIdentityId();
        var apiKey = authType == AzureAuthenticationType.ApiKey ? connection.GetApiKey() : null;
        var apiKeyHash = apiKey is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
        var enableLogging = options?.EnableLogging ?? false;
        var enableMessageLogging = options?.EnableMessageLogging ?? false;
        var enableMessageContentLogging = options?.EnableMessageContentLogging ?? false;
        var enableDefaultRetryPolicy = options?.EnableDefaultRetryPolicy ?? true;
        var maxRetryAttempts = Math.Max(options?.MaxRetryAttempts ?? 5, 0);
        var retryDelay = options?.RateLimitRetryDelay ?? TimeSpan.FromSeconds(1);
        var backoffType = options?.BackoffType ?? AzureRetryBackoffType.Exponential;
        var useJitter = options?.UseJitter ?? true;
        var maxRetryDelay = options?.MaxRetryDelay ?? TimeSpan.FromSeconds(32);
        var cacheKey = $"{endpoint.AbsoluteUri}|{authType}|{identityId}|{apiKeyHash}|{enableLogging}|{enableMessageLogging}|{enableMessageContentLogging}|{enableDefaultRetryPolicy}|{maxRetryAttempts}|{retryDelay}|{backoffType}|{useJitter}|{maxRetryDelay}";

        return _clientCache.GetOrAdd(cacheKey, _ =>
        {
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = endpoint,
                ClientLoggingOptions = new ClientLoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    EnableLogging = enableLogging,
                    EnableMessageLogging = enableMessageLogging,
                    EnableMessageContentLogging = enableMessageContentLogging,
                },
            };

            if (enableDefaultRetryPolicy)
            {
                clientOptions.RetryPolicy = new CrestAppsAzureOpenAIClientRetryPolicy(
                    maxRetryAttempts,
                    retryDelay,
                    backoffType,
                    useJitter,
                    maxRetryDelay,
                    enableLogging,
                    loggerFactory);
            }

            return apiKey is null
                ? new OpenAIClient(new BearerTokenPolicy(CreateTokenCredential(authType, identityId), CognitiveServicesScope), clientOptions)
                : new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        });
    }

    /// <summary>
    /// Creates the client that serves realtime sessions. Every session puts the connection's credential on its
    /// WebSocket handshake: the API key, or a Microsoft Entra ID token acquired when the session starts.
    /// </summary>
    /// <param name="connection">The connection.</param>
    public static RealtimeClient CreateRealtimeClient(AIProviderConnectionEntry connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var clientOptions = new RealtimeClientOptions
        {
            Endpoint = GetEndpoint(connection),
        };
        var authType = connection.GetAzureAuthenticationType();

        if (authType == AzureAuthenticationType.ApiKey)
        {
            var apiKey = connection.GetApiKey();
            var header = new KeyValuePair<string, string>(ApiKeyHeaderName, apiKey);

            return new AzureRealtimeClient(
                ApiKeyAuthenticationPolicy.CreateHeaderApiKeyPolicy(new ApiKeyCredential(apiKey), ApiKeyHeaderName),
                clientOptions,
                _ => ValueTask.FromResult(header));
        }

        var tokenCredential = CreateTokenCredential(authType, connection.GetIdentityId());

        return new AzureRealtimeClient(
            new BearerTokenPolicy(tokenCredential, CognitiveServicesScope),
            clientOptions,
            async cancellationToken =>
            {
                var token = await tokenCredential.GetTokenAsync(new TokenRequestContext(_cognitiveServicesScopes), cancellationToken);

                return new KeyValuePair<string, string>("Authorization", $"Bearer {token.Token}");
            });
    }

    /// <summary>
    /// Creates the Microsoft Entra ID credential for a connection that does not use an API key.
    /// </summary>
    /// <param name="authType">The connection's authentication type.</param>
    /// <param name="identityId">The client id of a user-assigned managed identity, or <see langword="null"/>.</param>
    private static TokenCredential CreateTokenCredential(AzureAuthenticationType authType, string identityId)
    {
        return authType switch
        {
            AzureAuthenticationType.ManagedIdentity => new ManagedIdentityCredential(string.IsNullOrEmpty(identityId) ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(identityId)),
            AzureAuthenticationType.Default => new DefaultAzureCredential(),
            _ => throw new NotSupportedException("The provided authentication type is not supported."),
        };
    }
}
