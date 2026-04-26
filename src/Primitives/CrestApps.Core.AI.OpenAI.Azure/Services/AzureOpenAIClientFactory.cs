using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using Azure.AI.OpenAI;
using Azure.Identity;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.Azure;
using CrestApps.Core.Azure.Models;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.OpenAI.Azure.Services;

internal static class AzureOpenAIClientFactory
{
    private static readonly ConcurrentDictionary<string, AzureOpenAIClient> _clientCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Clears the cached Azure OpenAI client instances.
    /// </summary>
    public static void ClearCache() => _clientCache.Clear();

    /// <summary>
    /// Creates the operation.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="options">The options.</param>
    public static AzureOpenAIClient Create(
        AIProviderConnectionEntry connection,
        ILoggerFactory loggerFactory,
        AzureClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var endpoint = connection.GetEndpoint();
        var authType = connection.GetAzureAuthenticationType();
        var identityId = connection.GetIdentityId();
        var cacheKey = $"{endpoint.AbsoluteUri}|{authType}|{identityId}";

        return _clientCache.GetOrAdd(cacheKey, _ =>
                {
                    var clientOptions = new AzureOpenAIClientOptions
                    {
                        ClientLoggingOptions = new ClientLoggingOptions
                        {
                            LoggerFactory = loggerFactory,
                            EnableLogging = options?.EnableLogging ?? false,
                            EnableMessageLogging = options?.EnableMessageLogging ?? false,
                            EnableMessageContentLogging = options?.EnableMessageContentLogging ?? false,
                        },
                    };

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
