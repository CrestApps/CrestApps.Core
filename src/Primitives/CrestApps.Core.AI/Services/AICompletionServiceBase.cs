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
    /// <param name="purpose">The purpose.</param>
    /// <param name="providerName">The provider name.</param>
    /// <param name="deploymentName">The deployment name.</param>
    protected virtual async ValueTask<AIDeployment> ResolveDeploymentAsync(
        AIDeploymentPurpose purpose,
        string providerName,
        string deploymentName = null)
    {
        if (DeploymentResolver != null)
        {
            var deployment = await DeploymentResolver.ResolveOrDefaultAsync(
                purpose,
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
    /// Resolves the deployment that serves the given completion request. A request marked as a
    /// background utility completion resolves the utility deployment first and falls back to the chat
    /// deployment when no utility deployment is configured.
    /// </summary>
    /// <param name="context">The completion context of the current request.</param>
    /// <param name="providerName">The provider name.</param>
    protected virtual async ValueTask<AIDeployment> ResolveRequestDeploymentAsync(
        AICompletionContext context,
        string providerName)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.IsUtilityCompletion)
        {
            var utilityDeployment = await ResolveDeploymentAsync(AIDeploymentPurpose.Utility, providerName, context.UtilityDeploymentName);

            if (utilityDeployment != null)
            {
                return utilityDeployment;
            }
        }

        return await ResolveDeploymentAsync(AIDeploymentPurpose.Chat, providerName, context.ChatDeploymentName);
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
