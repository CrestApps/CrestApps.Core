using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.AI.OpenAI;
using Azure.Identity;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Azure;
using CrestApps.Core.Azure.Models;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.OpenAI.Azure.Services;

internal static class AzureOpenAIClientFactory
{
    /// <summary>
    /// Gets the Azure OpenAI client for the connection from the container's cache, and builds it the first time. The
    /// client sends its requests through the <see cref="AzureOpenAIConstants.HttpClientName"/> client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="options">The options.</param>
    /// <param name="httpClientFactory">The factory of the client the SDK client sends its requests through.</param>
    /// <param name="clientCache">The cache that holds the SDK clients the container has built.</param>
    public static AzureOpenAIClient Create(
        AIProviderConnectionEntry connection,
        ILoggerFactory loggerFactory,
        AzureClientOptions options,
        IHttpClientFactory httpClientFactory,
        AIProviderClientCache clientCache)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(clientCache);

        var endpoint = connection.GetEndpoint();
        var authType = connection.GetAzureAuthenticationType();
        var identityId = connection.GetIdentityId();
        var apiKeyHash = authType == AzureAuthenticationType.ApiKey ? AIProviderClientCache.HashSecret(connection.GetApiKey()) : null;
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

        return clientCache.GetOrAdd(cacheKey, () =>
                {
                    var clientOptions = new AzureOpenAIClientOptions
                    {
                        Transport = new HttpClientPipelineTransport(httpClientFactory.CreateClient(AzureOpenAIConstants.HttpClientName)),
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

                    return authType switch
                    {
                        AzureAuthenticationType.ApiKey => new AzureOpenAIClient(endpoint, new ApiKeyCredential(connection.GetApiKey()), clientOptions),
                        AzureAuthenticationType.ManagedIdentity => new AzureOpenAIClient(endpoint, new ManagedIdentityCredential(string.IsNullOrEmpty(identityId) ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(identityId)), clientOptions),
                        AzureAuthenticationType.Default => new AzureOpenAIClient(endpoint, new DefaultAzureCredential(), clientOptions),
                        _ => throw new NotSupportedException("The provided authentication type is not supported."),
                    };
                });
    }
}
