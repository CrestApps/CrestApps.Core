#pragma warning disable OPENAI001 // The Responses types from the OpenAI SDK are for evaluation purposes only.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using Azure.Identity;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using CrestApps.Core.Azure;
using CrestApps.Core.Azure.Models;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace CrestApps.Core.AI.OpenAI.Azure.Responses;

/// <summary>
/// Builds an <see cref="IChatClient"/> for the Azure OpenAI Responses endpoint (<c>/openai/v1/responses</c>) from a
/// provider connection, resolving the endpoint and authentication (API key or Microsoft Entra ID token).
/// </summary>
/// <remarks>
/// This is a temporary client used because the current <c>Azure.AI.OpenAI</c> package targets an older
/// <c>OpenAI</c> SDK than the one <c>Microsoft.Extensions.AI.OpenAI</c> requires, so
/// <c>AzureOpenAIClient.GetResponsesClient()</c> throws <see cref="MissingMethodException"/> at runtime. Delete the
/// whole <c>Responses</c> folder and route back through the SDK once a compatible <c>Azure.AI.OpenAI</c> ships.
/// </remarks>
internal static class AzureResponsesClientFactory
{
    /// <summary>
    /// The token scope used for Microsoft Entra ID authentication against Azure OpenAI.
    /// </summary>
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";

    private static readonly ConcurrentDictionary<string, OpenAIClient> _clientCache = new(StringComparer.Ordinal);

    public static IChatClient Create(
        AIProviderConnectionEntry connection,
        string deploymentName,
        ILoggerFactory loggerFactory,
        AzureClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var endpoint = new Uri(connection.GetEndpoint(), "/openai/v1/");
        var authType = connection.GetAzureAuthenticationType();
        var identityId = connection.GetIdentityId();
        var enableLogging = options?.EnableLogging ?? false;
        var enableDefaultRetryPolicy = options?.EnableDefaultRetryPolicy ?? true;
        var cacheKey = $"{endpoint.AbsoluteUri}|{authType}|{identityId}|{enableLogging}|{enableDefaultRetryPolicy}";

        var client = _clientCache.GetOrAdd(cacheKey, _ =>
        {
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = endpoint,
                ClientLoggingOptions = new ClientLoggingOptions
                {
                    LoggerFactory = loggerFactory,
                    EnableLogging = enableLogging,
                    EnableMessageLogging = options?.EnableMessageLogging ?? false,
                    EnableMessageContentLogging = options?.EnableMessageContentLogging ?? false,
                },
            };

            if (enableDefaultRetryPolicy)
            {
                clientOptions.RetryPolicy = new CrestAppsAzureOpenAIClientRetryPolicy(
                    Math.Max(options?.MaxRetryAttempts ?? 5, 0),
                    options?.RateLimitRetryDelay ?? TimeSpan.FromSeconds(1),
                    options?.BackoffType ?? AzureRetryBackoffType.Exponential,
                    options?.UseJitter ?? true,
                    options?.MaxRetryDelay ?? TimeSpan.FromSeconds(32),
                    enableLogging,
                    loggerFactory);
            }

            return authType switch
            {
                AzureAuthenticationType.ApiKey => new OpenAIClient(new ApiKeyCredential(connection.GetApiKey()), clientOptions),
                AzureAuthenticationType.ManagedIdentity => new OpenAIClient(new BearerTokenPolicy(new ManagedIdentityCredential(string.IsNullOrEmpty(identityId) ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(identityId)), CognitiveServicesScope), clientOptions),
                AzureAuthenticationType.Default => new OpenAIClient(new BearerTokenPolicy(new DefaultAzureCredential(), CognitiveServicesScope), clientOptions),
                _ => throw new NotSupportedException("The provided authentication type is not supported."),
            };
        });

        return client.GetResponsesClient().AsIChatClient(deploymentName);
    }
}
