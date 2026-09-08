using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Capabilities;

/// <summary>
/// Extension methods that add model parameter handling to a <see cref="ChatClientBuilder"/> pipeline.
/// </summary>
public static class AIDeploymentParameterChatClientBuilderExtensions
{
    /// <summary>
    /// Applies the model parameter values selected in the given scope to every request issued through
    /// the built client.
    /// </summary>
    /// <param name="builder">The chat client builder.</param>
    /// <param name="applier">The parameter applier. When <see langword="null"/>, the builder is returned unchanged.</param>
    /// <param name="deployment">The deployment resolved for the request.</param>
    /// <param name="completionContext">The completion context holding the selected values.</param>
    /// <param name="scope">The set of selected values to apply.</param>
    public static ChatClientBuilder UseModelParameters(
        this ChatClientBuilder builder,
        IAIDeploymentParameterApplier applier,
        AIDeployment deployment,
        AICompletionContext completionContext,
        AIDeploymentParameterScope scope)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (applier is null || completionContext is null)
        {
            return builder;
        }

        return builder.Use(innerClient => new DeploymentParameterApplyingChatClient(innerClient, applier, deployment, completionContext, scope));
    }

    /// <summary>
    /// Applies the utility model parameter values selected on the given profile to every request issued
    /// through the built client. Background completions such as planning, data extraction, and
    /// post-session processing run against the profile's utility deployment.
    /// </summary>
    /// <param name="builder">The chat client builder.</param>
    /// <param name="applier">The parameter applier. When <see langword="null"/>, the builder is returned unchanged.</param>
    /// <param name="deployment">The utility deployment resolved for the request.</param>
    /// <param name="profile">The profile holding the selected values.</param>
    public static ChatClientBuilder UseUtilityModelParameters(
        this ChatClientBuilder builder,
        IAIDeploymentParameterApplier applier,
        AIDeployment deployment,
        AIProfile profile)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (applier is null || profile is null)
        {
            return builder;
        }

        var completionContext = new AICompletionContext
        {
            ChatDeploymentName = profile.ChatDeploymentName,
            UtilityDeploymentName = profile.UtilityDeploymentName,
            IsUtilityCompletion = true,
        };

        if (profile.TryGet<AIDeploymentParametersMetadata>(out var metadata))
        {
            completionContext.ApplyModelParameters(metadata);
        }

        return builder.UseModelParameters(applier, deployment, completionContext, AIDeploymentParameterScope.Utility);
    }
}
