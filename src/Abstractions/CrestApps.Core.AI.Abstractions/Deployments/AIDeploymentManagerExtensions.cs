using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Deployments;

/// <summary>
/// Extension methods for <see cref="IAIDeploymentManager"/> that provide convenience deployment resolution helpers.
/// </summary>
public static class AIDeploymentManagerExtensions
{
    /// <summary>
    /// Resolves utility or default.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="utilityDeploymentName">The utility deployment name.</param>
    /// <param name="chatDeploymentName">The chat deployment name.</param>
    /// <param name="clientName">The client name.</param>
    public static async ValueTask<AIDeployment> ResolveUtilityOrDefaultAsync(
        this IAIDeploymentManager deploymentManager,
        string utilityDeploymentName = null,
        string chatDeploymentName = null,
        string clientName = null)
    {
        ArgumentNullException.ThrowIfNull(deploymentManager);

        return await deploymentManager.ResolveOrDefaultAsync(
            AIDeploymentType.Utility,
            utilityDeploymentName,
            clientName)
        ?? await deploymentManager.ResolveOrDefaultAsync(
            AIDeploymentType.Chat,
            chatDeploymentName,
            clientName);
    }

    /// <summary>
    /// Resolves the operation.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="type">The type.</param>
    /// <param name="deploymentName">The deployment name.</param>
    /// <param name="clientName">The client name.</param>
    public static async ValueTask<AIDeployment> ResolveAsync(
        this IAIDeploymentManager deploymentManager,
        AIDeploymentType type,
        string deploymentName = null,
        string clientName = null)
    {
        ArgumentNullException.ThrowIfNull(deploymentManager);

        var deployment = await deploymentManager.ResolveOrDefaultAsync(type, deploymentName, clientName);

        return deployment
            ?? throw new InvalidOperationException($"Unable to resolve an AI deployment for type '{type}' with deploymentName '{deploymentName ?? "(null)"}' and clientName '{clientName ?? "(null)"}'.");
    }

    /// <summary>
    /// Resolves utility.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="utilityDeploymentName">The utility deployment name.</param>
    /// <param name="chatDeploymentName">The chat deployment name.</param>
    /// <param name="clientName">The client name.</param>
    public static async ValueTask<AIDeployment> ResolveUtilityAsync(
        this IAIDeploymentManager deploymentManager,
        string utilityDeploymentName = null,
        string chatDeploymentName = null,
        string clientName = null)
    {
        ArgumentNullException.ThrowIfNull(deploymentManager);

        var deployment = await deploymentManager.ResolveUtilityOrDefaultAsync(utilityDeploymentName, chatDeploymentName, clientName);

        return deployment
            ?? throw new InvalidOperationException($"Unable to resolve a utility AI deployment using utilityDeploymentName '{utilityDeploymentName ?? "(null)"}', chatDeploymentName '{chatDeploymentName ?? "(null)"}', and clientName '{clientName ?? "(null)"}'.");
    }
}
