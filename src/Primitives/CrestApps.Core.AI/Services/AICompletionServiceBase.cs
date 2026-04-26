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

    /// <summary>
    /// Initializes a new instance of the <see cref="AICompletionServiceBase"/> class.
    /// </summary>
    /// <param name="aiTemplateService">The ai template service.</param>
    protected AICompletionServiceBase(ITemplateService aiTemplateService)
    {
        AITemplateService = aiTemplateService;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AICompletionServiceBase"/> class.
    /// </summary>
    /// <param name="aiTemplateService">The ai template service.</param>
    /// <param name="deploymentResolver">The deployment resolver.</param>
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
    /// <param name="type">The type.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="deploymentName">The deployment name.</param>
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

    /// <summary>
    /// Gets total messages to skip.
    /// </summary>
    /// <param name="totalMessages">The total messages.</param>
    /// <param name="pastMessageCount">The past message count.</param>
    protected static int GetTotalMessagesToSkip(int totalMessages, int pastMessageCount)
    {
        if (pastMessageCount > 0 && totalMessages > pastMessageCount)
        {
            return totalMessages - pastMessageCount;
        }

        return 0;
    }

    /// <summary>
    /// Gets deployment.
    /// </summary>
    /// <param name="content">The content.</param>
    protected virtual Task<AIDeployment> GetDeploymentAsync(AICompletionContext content)
    {
        return Task.FromResult<AIDeployment>(null);
    }
}
