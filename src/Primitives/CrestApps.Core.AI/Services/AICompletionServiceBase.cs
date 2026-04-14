using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Templates.Services;

namespace CrestApps.Core.AI.Services;

public abstract class AICompletionServiceBase
{
    protected readonly AIProviderOptions ProviderOptions;
    protected readonly ITemplateService AITemplateService;
    protected readonly IAIDeploymentManager DeploymentResolver;

    protected AICompletionServiceBase(
        AIProviderOptions providerOptions,
        ITemplateService aiTemplateService)
    {
        ProviderOptions = providerOptions;
        AITemplateService = aiTemplateService;
    }

    protected AICompletionServiceBase(
        AIProviderOptions providerOptions,
        ITemplateService aiTemplateService,
        IAIDeploymentManager deploymentResolver)
    : this(providerOptions, aiTemplateService)
    {
        DeploymentResolver = deploymentResolver;
    }

    protected virtual string GetDefaultDeploymentName(AIProvider provider, string connectionName)
    {
        if (connectionName is not null && provider.Connections.TryGetValue(connectionName, out var connection))
        {
            var deploymentName = connection.GetLegacyChatDeploymentName();

            if (!string.IsNullOrEmpty(deploymentName))
            {
                return deploymentName;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a deployment using the <see cref="IAIDeploymentManager"/>
    /// with fallback to legacy connection entry values when they are still present.
    /// </summary>
    protected virtual async ValueTask<AIDeployment> ResolveDeploymentAsync(
        AIDeploymentType type,
        AIProvider provider,
        string providerName,
        string connectionName,
        string deploymentName = null)
    {
        if (DeploymentResolver != null)
        {
            var deployment = await DeploymentResolver.ResolveOrDefaultAsync(
                type,
                deploymentName: deploymentName,
                clientName: providerName,
                connectionName: connectionName);

            if (deployment != null)
            {
                // If the deployment has no connection name, fall back to the requested connectionName.
                if (string.IsNullOrEmpty(deployment.ConnectionName) && !string.IsNullOrEmpty(connectionName))
                {
                    deployment.ConnectionName = connectionName;
                }

                return deployment;
            }
        }

        // Fall back to legacy dictionary-based resolution.
        var legacyModelName = GetDefaultDeploymentName(provider, connectionName);

        if (string.IsNullOrEmpty(legacyModelName))
        {
            return null;
        }

        return new AIDeployment
        {
            ClientName = providerName,
            ConnectionName = connectionName,
            ModelName = legacyModelName,
        };
    }

    protected static int GetTotalMessagesToSkip(int totalMessages, int pastMessageCount)
    {
        if (pastMessageCount > 0 && totalMessages > pastMessageCount)
        {
            return totalMessages - pastMessageCount;
        }

        return 0;
    }

    protected virtual Task<AIDeployment> GetDeploymentAsync(AICompletionContext content)
    {
        return Task.FromResult<AIDeployment>(null);
    }
}
