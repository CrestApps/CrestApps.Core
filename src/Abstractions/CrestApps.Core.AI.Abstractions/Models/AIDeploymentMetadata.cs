namespace CrestApps.Core.AI.Models;

/// <summary>
/// Metadata stored on <see cref="AIDeployment"/> describing the features and configurable parameters
/// exposed by the underlying model. Editors, validation, and runtime request generation are driven from
/// this metadata instead of provider or model name detection.
/// </summary>
public sealed class AIDeploymentMetadata
{
    /// <summary>
    /// Gets or sets the technical names of the registered model features supported by this deployment.
    /// </summary>
    public string[] Features { get; set; } = [];

    /// <summary>
    /// Gets or sets the supported model parameters keyed by their registered technical name.
    /// A parameter that is not present in this dictionary is not supported by the deployment and is
    /// never rendered by editors or sent to the provider.
    /// </summary>
    public Dictionary<string, AIDeploymentParameter> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether the deployment supports the given registered feature.
    /// </summary>
    /// <param name="featureName">The technical name of the feature.</param>
    public bool SupportsFeature(string featureName)
    {
        if (string.IsNullOrWhiteSpace(featureName) || Features is not { Length: > 0 })
        {
            return false;
        }

        return Features.Any(feature => string.Equals(feature, featureName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether the deployment supports the given registered parameter.
    /// </summary>
    /// <param name="parameterName">The technical name of the parameter.</param>
    public bool SupportsParameter(string parameterName)
    {
        return !string.IsNullOrWhiteSpace(parameterName) &&
            Parameters is not null &&
            Parameters.ContainsKey(parameterName);
    }
}
