using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Templates.Services;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the AI Completion Service Base.
/// </summary>
public abstract class AICompletionServiceBase
{
    protected readonly ITemplateService AITemplateService;
    protected readonly IAIDeploymentManager DeploymentResolver;

    protected AICompletionServiceBase(ITemplateService aiTemplateService)
    {
        AITemplateService = aiTemplateService;
    }

    protected AICompletionServiceBase(
        ITemplateService aiTemplateService,
        IAIDeploymentManager deploymentResolver)
    : this(aiTemplateService)
    {
        DeploymentResolver = deploymentResolver;
    }

    /// <summary>
    /// Resolves a deployment using the <see cref="IAIDeploymentManager"/>.
    /// </summary>
    protected virtual async ValueTask<AIDeployment> ResolveDeploymentAsync(
        AIDeploymentType type,
        string providerName,
        string deploymentName = null)
    {
        if (DeploymentResolver != null)
        {
            var deployment = await DeploymentResolver.ResolveOrDefaultAsync(
                type,
                deploymentName: deploymentName,
                clientName: providerName);

            if (deployment != null)
            {
                return deployment;
            }
        }

        return null;
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
