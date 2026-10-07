using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Meters a client that was not created through <see cref="Clients.IAIClientFactory"/>, the same way the factory
/// meters the clients it creates. Wrap the provider's client before adding any middleware, so a tool-calling loop
/// built on top of it is metered once per round trip.
/// </summary>
public static class AIUsageMetering
{
    /// <summary>
    /// Meters a chat client.
    /// </summary>
    /// <param name="innerClient">The provider's chat client.</param>
    /// <param name="clientName">The client (provider) name to record.</param>
    /// <param name="connectionName">The connection name to record, or <see langword="null"/>.</param>
    /// <param name="deploymentName">The deployment or model the requests are sent to.</param>
    /// <param name="serviceProvider">The service provider that resolves the usage observers.</param>
    /// <param name="defaultPurpose">The purpose to record when neither the request nor an <see cref="Completions.AIUsageScope"/> names one, or <see langword="null"/>.</param>
    public static IChatClient Meter(
        IChatClient innerClient,
        string clientName,
        string connectionName,
        string deploymentName,
        IServiceProvider serviceProvider,
        string defaultPurpose = null)
    {
        ArgumentNullException.ThrowIfNull(innerClient);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return new AICompletionUsageTrackingChatClient(innerClient, CreateRecorder(clientName, connectionName, deploymentName, serviceProvider, defaultPurpose));
    }

    /// <summary>
    /// Meters an embedding generator.
    /// </summary>
    /// <param name="innerGenerator">The provider's embedding generator.</param>
    /// <param name="clientName">The client (provider) name to record.</param>
    /// <param name="connectionName">The connection name to record, or <see langword="null"/>.</param>
    /// <param name="deploymentName">The deployment or model the requests are sent to.</param>
    /// <param name="serviceProvider">The service provider that resolves the usage observers.</param>
    /// <param name="defaultPurpose">The purpose to record when neither the request nor an <see cref="Completions.AIUsageScope"/> names one, or <see langword="null"/>.</param>
    public static IEmbeddingGenerator<string, Embedding<float>> Meter(
        IEmbeddingGenerator<string, Embedding<float>> innerGenerator,
        string clientName,
        string connectionName,
        string deploymentName,
        IServiceProvider serviceProvider,
        string defaultPurpose = null)
    {
        ArgumentNullException.ThrowIfNull(innerGenerator);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return new AIUsageTrackingEmbeddingGenerator(innerGenerator, CreateRecorder(clientName, connectionName, deploymentName, serviceProvider, defaultPurpose));
    }

    private static AIUsageRecorder CreateRecorder(
        string clientName,
        string connectionName,
        string deploymentName,
        IServiceProvider serviceProvider,
        string defaultPurpose)
    {
        return new AIUsageRecorder(
            new AIDeploymentUsageInfo(clientName, connectionName, deploymentName, deploymentName),
            serviceProvider,
            serviceProvider.GetRequiredService<ILogger<AIUsageRecorder>>(),
            defaultPurpose);
    }
}
