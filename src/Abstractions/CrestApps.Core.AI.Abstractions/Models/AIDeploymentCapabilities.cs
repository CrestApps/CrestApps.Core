namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents the effective capabilities of an <see cref="AIDeployment"/> after the registered
/// definitions have been merged with the deployment specific metadata.
/// </summary>
public sealed class AIDeploymentCapabilities
{
    /// <summary>
    /// Gets an instance that exposes no features and no parameters.
    /// </summary>
    public static AIDeploymentCapabilities Empty { get; } = new AIDeploymentCapabilities([], []);

    private readonly Dictionary<string, AIModelParameterDescriptor> _parameters;
    private readonly HashSet<string> _features;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIDeploymentCapabilities"/> class.
    /// </summary>
    /// <param name="features">The features exposed by the deployment.</param>
    /// <param name="parameters">The effective parameters exposed by the deployment.</param>
    public AIDeploymentCapabilities(
        IEnumerable<AIModelFeatureDescriptor> features,
        IEnumerable<AIModelParameterDescriptor> parameters)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(parameters);

        Features = [.. features.OrderBy(feature => feature.Order).ThenBy(feature => feature.Name, StringComparer.OrdinalIgnoreCase)];
        Parameters = [.. parameters.OrderBy(parameter => parameter.Order).ThenBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)];
        _features = new HashSet<string>(Features.Select(feature => feature.Name), StringComparer.OrdinalIgnoreCase);
        _parameters = Parameters.ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the features exposed by the deployment.
    /// </summary>
    public IReadOnlyList<AIModelFeatureDescriptor> Features { get; }

    /// <summary>
    /// Gets the effective parameters exposed by the deployment.
    /// </summary>
    public IReadOnlyList<AIModelParameterDescriptor> Parameters { get; }

    /// <summary>
    /// Determines whether the deployment exposes the given feature.
    /// </summary>
    /// <param name="featureName">The technical name of the feature.</param>
    public bool SupportsFeature(string featureName)
    {
        return !string.IsNullOrWhiteSpace(featureName) && _features.Contains(featureName);
    }

    /// <summary>
    /// Gets the effective descriptor of the given parameter, or <see langword="null"/> when the
    /// deployment does not expose it.
    /// </summary>
    /// <param name="parameterName">The technical name of the parameter.</param>
    public AIModelParameterDescriptor GetParameter(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return null;
        }

        return _parameters.TryGetValue(parameterName, out var descriptor)
            ? descriptor
            : null;
    }

    /// <summary>
    /// Determines whether the deployment exposes the given parameter.
    /// </summary>
    /// <param name="parameterName">The technical name of the parameter.</param>
    public bool SupportsParameter(string parameterName)
    {
        return GetParameter(parameterName) is not null;
    }
}
