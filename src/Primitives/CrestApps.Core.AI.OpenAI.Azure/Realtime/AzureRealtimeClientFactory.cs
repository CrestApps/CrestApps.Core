#pragma warning disable MEAI001 // The realtime types from Microsoft.Extensions.AI are for evaluation purposes only.
#nullable enable
using Azure.Core;
using Azure.Identity;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Azure.Models;
using CrestApps.Core.Infrastructure;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.OpenAI.Azure.Realtime;

/// <summary>
/// Builds an <see cref="AzureRealtimeClient"/> from a provider connection, resolving the endpoint, API version,
/// deployment, and authentication (API key or Microsoft Entra ID token).
/// </summary>
/// <remarks>
/// Temporary transport; see <see cref="AzureRealtimeClient"/> for the rationale and removal plan.
/// </remarks>
internal static class AzureRealtimeClientFactory
{
    /// <summary>The token scope used for Microsoft Entra ID authentication against Azure OpenAI.</summary>
    private static readonly string[] _cognitiveServicesScopes = ["https://cognitiveservices.azure.com/.default"];

    public static IRealtimeClient Create(AIProviderConnectionEntry connection, string deploymentName)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrEmpty(deploymentName))
        {
            deploymentName = connection.GetStringValue("RealtimeDeploymentName", false);
        }

        if (string.IsNullOrEmpty(deploymentName))
        {
            throw new ArgumentException("A realtime deployment name must be provided, either directly or as a default in the connection settings.");
        }

        var endpoint = connection.GetEndpoint();

        return new AzureRealtimeClient(endpoint, deploymentName, CreateAuthHeaderFactory(connection));
    }

    private static Func<CancellationToken, ValueTask<KeyValuePair<string, string>>> CreateAuthHeaderFactory(AIProviderConnectionEntry connection)
    {
        var authType = CrestApps.Core.Azure.DictionaryExtensions.GetAzureAuthenticationType(connection);

        if (authType == AzureAuthenticationType.ApiKey)
        {
            var header = new KeyValuePair<string, string>("api-key", connection.GetApiKey());

            return _ => new ValueTask<KeyValuePair<string, string>>(header);
        }

        var identityId = CrestApps.Core.Azure.DictionaryExtensions.GetIdentityId(connection);

        TokenCredential credential = authType switch
        {
            AzureAuthenticationType.ManagedIdentity => new ManagedIdentityCredential(string.IsNullOrEmpty(identityId) ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(identityId)),
            _ => new DefaultAzureCredential(),
        };

        return async cancellationToken =>
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(_cognitiveServicesScopes), cancellationToken);

            return new KeyValuePair<string, string>("Authorization", $"Bearer {token.Token}");
        };
    }
}
