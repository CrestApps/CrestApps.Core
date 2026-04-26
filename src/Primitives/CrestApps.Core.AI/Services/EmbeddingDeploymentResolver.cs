using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Provides functionality for embedding Deployment Resolver.
/// </summary>
public static class EmbeddingDeploymentResolver
{
    /// <summary>
    /// Find embedding deployments embedding deployment.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="metadata">The metadata.</param>
    /// <param name="deploymentIdOrName">The deployment id or name.</param>
    public static async Task<AIDeployment> FindEmbeddingDeploymentAsync(
        IAIDeploymentManager deploymentManager,
        DataSourceIndexProfileMetadata metadata,
        string deploymentIdOrName = null)
    {
        ArgumentNullException.ThrowIfNull(deploymentManager);

        var selector = string.IsNullOrWhiteSpace(deploymentIdOrName)
            ? metadata?.EmbeddingDeploymentId
            : deploymentIdOrName;

        if (!string.IsNullOrWhiteSpace(selector))
        {
            var deployment = await deploymentManager.FindByIdAsync(selector) ??
                await deploymentManager.FindByNameAsync(selector);

            if (deployment != null)
            {
                return deployment;
            }
        }

#pragma warning disable CS0618 // Type or member is obsolete
        if (metadata == null ||
            string.IsNullOrWhiteSpace(metadata.EmbeddingProviderName) ||
            string.IsNullOrWhiteSpace(metadata.EmbeddingConnectionName) ||
            string.IsNullOrWhiteSpace(metadata.EmbeddingDeploymentName))
        {
            return null;
        }

        var legacyDeployment = await deploymentManager.FindByNameAsync(metadata.EmbeddingDeploymentName);

        if (IsMatchingDeployment(legacyDeployment, metadata))
        {
            return legacyDeployment;
        }

        var deployments = await deploymentManager.GetAllAsync(metadata.EmbeddingProviderName);

        return deployments.FirstOrDefault(deployment =>
                    deployment.SupportsType(AIDeploymentType.Embedding) &&
                    (string.Equals(deployment.Name, metadata.EmbeddingDeploymentName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(deployment.ModelName, metadata.EmbeddingDeploymentName, StringComparison.OrdinalIgnoreCase)));
#pragma warning restore CS0618 // Type or member is obsolete
    }

    /// <summary>
    /// Creates embedding generator.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="aiClientFactory">The ai client factory.</param>
    /// <param name="metadata">The metadata.</param>
    /// <param name="deploymentIdOrName">The deployment id or name.</param>
    public static async Task<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(
        IAIDeploymentManager deploymentManager,
        IAIClientFactory aiClientFactory,
        DataSourceIndexProfileMetadata metadata,
        string deploymentIdOrName = null)
    {
        ArgumentNullException.ThrowIfNull(deploymentManager);
        ArgumentNullException.ThrowIfNull(aiClientFactory);

        var deployment = await FindEmbeddingDeploymentAsync(deploymentManager, metadata, deploymentIdOrName);

        if (deployment != null)
        {
            return await aiClientFactory.CreateEmbeddingGeneratorAsync(deployment);
        }

#pragma warning disable CS0618 // Type or member is obsolete
        if (metadata == null ||
            string.IsNullOrWhiteSpace(metadata.EmbeddingProviderName) ||
            string.IsNullOrWhiteSpace(metadata.EmbeddingConnectionName) ||
            string.IsNullOrWhiteSpace(metadata.EmbeddingDeploymentName))
        {
            return null;
        }

        var legacyDeployment = new AIDeployment
        {
            ClientName = metadata.EmbeddingProviderName,
            ConnectionName = metadata.EmbeddingConnectionName,
            ModelName = metadata.EmbeddingDeploymentName,
        };

        return await aiClientFactory.CreateEmbeddingGeneratorAsync(legacyDeployment);
#pragma warning restore CS0618 // Type or member is obsolete
    }

    private static bool IsMatchingDeployment(AIDeployment deployment, DataSourceIndexProfileMetadata metadata)
    {
        if (deployment == null || !deployment.SupportsType(AIDeploymentType.Embedding))
        {
            return false;
        }

#pragma warning disable CS0618 // Type or member is obsolete

        return string.Equals(deployment.ClientName, metadata.EmbeddingProviderName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(deployment.ConnectionName, metadata.EmbeddingConnectionName, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(deployment.Name, metadata.EmbeddingDeploymentName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(deployment.ModelName, metadata.EmbeddingDeploymentName, StringComparison.OrdinalIgnoreCase));
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
