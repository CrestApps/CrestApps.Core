using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Capabilities;

/// <summary>
/// Provides the shared feature-enforcement logic that removes request options which depend on a
/// trained feature the deployment does not declare. The logic is used by both the completion
/// pipeline handler and the client-factory wrapper so that enforcement is identical on every path.
/// </summary>
internal static class ModelFeatureEnforcement
{
    /// <summary>
    /// Removes or corrects the standard, provider-agnostic options that require a trained feature the
    /// deployment does not declare (such as tools, a JSON response format, or reasoning), and coerces
    /// the reasoning effort to a supported value when reasoning is declared but the requested effort
    /// is not exposed.
    /// </summary>
    /// <param name="options">The chat options to sanitize.</param>
    /// <param name="capabilities">The effective capabilities of the deployment.</param>
    /// <param name="deploymentName">The deployment name used when logging removed options.</param>
    /// <param name="logger">The logger used to report removed options.</param>
    /// <returns><see langword="true"/> when at least one option was removed; otherwise <see langword="false"/>.</returns>
    public static bool Enforce(
        ChatOptions options,
        AIDeploymentCapabilities capabilities,
        string deploymentName,
        ILogger logger)
    {
        if (options is null || capabilities is null)
        {
            return false;
        }

        var changed = false;

        if (!capabilities.SupportsFeature(AIDeploymentFeatureNames.ToolCalling))
        {
            if (options.Tools is { Count: > 0 })
            {
                logger.LogWarning(
                    "Deployment '{Deployment}' does not declare the '{Feature}' feature. {Count} tool(s) were removed from the request.",
                    deploymentName, AIDeploymentFeatureNames.ToolCalling, options.Tools.Count);

                options.Tools = null;
                changed = true;
            }

            // Clear the tool mode even when no tools were supplied so an unsupported deployment never
            // receives a tool mode such as RequireAny, which some providers reject on its own.
            if (options.ToolMode is not null)
            {
                options.ToolMode = null;
                changed = true;
            }
        }

        if (!capabilities.SupportsFeature(AIDeploymentFeatureNames.StructuredOutputs) && options.ResponseFormat is ChatResponseFormatJson)
        {
            logger.LogWarning(
                "Deployment '{Deployment}' does not declare the '{Feature}' feature. The JSON response format was removed from the request.",
                deploymentName, AIDeploymentFeatureNames.StructuredOutputs);

            options.ResponseFormat = null;
            changed = true;
        }

        if (!capabilities.SupportsFeature(AIDeploymentFeatureNames.Reasoning))
        {
            // The deployment is not trained to reason, so no reasoning option may be sent to the
            // provider. Removing it prevents providers from rejecting the request outright.
            if (options.Reasoning is not null)
            {
                logger.LogWarning(
                    "Deployment '{Deployment}' does not declare the '{Feature}' feature. The reasoning options were removed from the request.",
                    deploymentName, AIDeploymentFeatureNames.Reasoning);

                options.Reasoning = null;
                changed = true;
            }
        }
        else if (options.Reasoning?.Effort is ReasoningEffort effort)
        {
            // Reasoning is supported, but the selected effort must still be exposed by the deployment
            // and be one of the values it allows; otherwise the provider may reject the request. Only
            // the effort is adjusted so any other reasoning state (such as Output) is preserved.
            var descriptor = capabilities.GetParameter(AIDeploymentParameterNames.ReasoningEffort);

            if (descriptor is null)
            {
                logger.LogWarning(
                    "Deployment '{Deployment}' does not expose the '{Parameter}' parameter. The reasoning effort '{Effort}' was removed from the request.",
                    deploymentName, AIDeploymentParameterNames.ReasoningEffort, effort);

                options.Reasoning.Effort = null;
                changed = true;
            }
            else if (!descriptor.IsValidValue(effort.ToString()))
            {
                if (!string.IsNullOrWhiteSpace(descriptor.DefaultValue) &&
                    Enum.TryParse<ReasoningEffort>(descriptor.DefaultValue, ignoreCase: true, out var fallback))
                {
                    logger.LogWarning(
                        "Deployment '{Deployment}' does not support the reasoning effort '{Effort}'. The deployment default '{Default}' is used instead.",
                        deploymentName, effort, fallback);

                    options.Reasoning.Effort = fallback;
                }
                else
                {
                    logger.LogWarning(
                        "Deployment '{Deployment}' does not support the reasoning effort '{Effort}'. The reasoning effort was removed from the request.",
                        deploymentName, effort);

                    options.Reasoning.Effort = null;
                }

                changed = true;
            }
        }

        return changed;
    }
}
