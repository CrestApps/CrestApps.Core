using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Deployments;

/// <summary>
/// Extension methods for <see cref="IAIDeploymentManager"/> that provide convenience deployment resolution helpers.
/// </summary>
public static class AIDeploymentManagerExtensions
{
    /// <summary>
    /// Resolves the deployment that serves background utility work, falling back to the chat deployment.
    /// </summary>
    /// <remarks>
    /// This is one ordered chain — explicit utility name, then the site default utility deployment, then the
    /// explicit chat name, then the site default chat deployment, and only then the first text-capable
    /// deployment. It is deliberately not two independent resolves joined with <c>??</c>: since both slots
    /// filter on the same text-generation capability, the utility resolve's own "first capable" tail would
    /// almost always answer first, silently routing summarization, data extraction, and query rewriting to
    /// an arbitrary model instead of the caller's configured chat deployment.
    /// </remarks>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="utilityDeploymentName">The utility deployment name.</param>
    /// <param name="chatDeploymentName">The chat deployment name.</param>
    /// <param name="clientName">The client name.</param>
    public static ValueTask<AIDeployment> ResolveUtilityOrDefaultAsync(
        this IAIDeploymentManager deploymentManager,
        string utilityDeploymentName = null,
        string chatDeploymentName = null,
        string clientName = null)
    {
        ArgumentNullException.ThrowIfNull(deploymentManager);

        return deploymentManager.ResolveSlotAsync(
            AIDeploymentSlotNames.Utility,
            utilityDeploymentName,
            clientName,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AIDeploymentSlotNames.Chat] = chatDeploymentName,
            });
    }

    /// <summary>
    /// Resolves the deployment that fills a named slot, or throws when nothing can fill it.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="slotName">The technical name of the slot. See <see cref="AIDeploymentSlotNames"/>.</param>
    /// <param name="deploymentName">The optional deployment name explicitly requested for this slot.</param>
    /// <param name="clientName">The optional client name used to scope the fallback.</param>
    public static async ValueTask<AIDeployment> ResolveSlotOrThrowAsync(
        this IAIDeploymentManager deploymentManager,
        string slotName,
        string deploymentName = null,
        string clientName = null)
    {
        ArgumentNullException.ThrowIfNull(deploymentManager);

        var deployment = await deploymentManager.ResolveSlotAsync(slotName, deploymentName, clientName);

        return deployment
            ?? throw new InvalidOperationException($"Unable to resolve an AI deployment for slot '{slotName}' with deploymentName '{deploymentName ?? "(null)"}' and clientName '{clientName ?? "(null)"}'.");
    }

    /// <summary>
    /// Gets the union of the text-capable deployments and the realtime (speech-to-speech) ones, as the
    /// editors' single "what can this profile talk to" picker used to offer them.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="clientName">The optional client name to further filter results.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <remarks>
    /// <para>
    /// This describes the pre-split picker. A profile once answered "text or voice?" by which deployment it
    /// named, so one picker had to offer both kinds. It no longer does: the chat mode decides whether the
    /// conversation is spoken, the chat deployment means only "the text model this profile talks to" — the
    /// <see cref="AIDeploymentSlotNames.Chat"/> slot, which excludes realtime deployments because they cannot
    /// serve a text completion — and the conversation deployment names the model that carries a spoken one.
    /// </para>
    /// <para>
    /// Kept because it is public API on an abstractions package. No editor calls it.
    /// </para>
    /// </remarks>
    public static async ValueTask<IEnumerable<AIDeployment>> GetConversationalDeploymentsAsync(
        this IAIDeploymentManager deploymentManager,
        string clientName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deploymentManager);

        var textCapable = await deploymentManager.GetAllBySlotAsync(AIDeploymentSlotNames.Chat, clientName, cancellationToken);
        var realtimeCapable = await deploymentManager.GetAllBySlotAsync(AIDeploymentSlotNames.Realtime, clientName, cancellationToken);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<AIDeployment>();

        foreach (var deployment in textCapable.Concat(realtimeCapable))
        {
            if (!string.IsNullOrEmpty(deployment.Name) && seen.Add(deployment.Name))
            {
                results.Add(deployment);
            }
        }

        return results;
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
