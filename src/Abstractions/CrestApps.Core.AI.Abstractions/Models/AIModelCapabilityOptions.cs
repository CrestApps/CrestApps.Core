using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Holds the model features and model parameters that modules contribute to the framework.
/// Deployments reference these registered definitions through <see cref="AIDeploymentModelMetadata"/>.
/// </summary>
public sealed class AIModelCapabilityOptions
{
    /// <summary>
    /// Gets the registered model features keyed by their technical name.
    /// </summary>
    public IDictionary<string, AIModelFeatureDescriptor> Features { get; } = new Dictionary<string, AIModelFeatureDescriptor>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the registered model parameters keyed by their technical name.
    /// </summary>
    public IDictionary<string, AIModelParameterDescriptor> Parameters { get; } = new Dictionary<string, AIModelParameterDescriptor>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a model feature, or updates the definition when the feature is already registered.
    /// </summary>
    /// <param name="name">The technical name of the feature.</param>
    /// <param name="displayName">The display text shown to operators.</param>
    /// <param name="configure">An optional delegate used to further configure the descriptor.</param>
    public AIModelCapabilityOptions AddFeature(string name, LocalizedString displayName, Action<AIModelFeatureDescriptor> configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!Features.TryGetValue(name, out var descriptor))
        {
            descriptor = new AIModelFeatureDescriptor();
            Features[name] = descriptor;
        }

        descriptor.Name = name;

        if (displayName is not null)
        {
            descriptor.DisplayName = displayName;
        }

        descriptor.DisplayName ??= new LocalizedString(name, name);
        configure?.Invoke(descriptor);

        return this;
    }

    /// <summary>
    /// Adds a model parameter, or updates the definition when the parameter is already registered.
    /// </summary>
    /// <param name="name">The technical name of the parameter.</param>
    /// <param name="displayName">The display text shown to operators.</param>
    /// <param name="configure">An optional delegate used to further configure the descriptor.</param>
    public AIModelCapabilityOptions AddParameter(string name, LocalizedString displayName, Action<AIModelParameterDescriptor> configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!Parameters.TryGetValue(name, out var descriptor))
        {
            descriptor = new AIModelParameterDescriptor();
            Parameters[name] = descriptor;
        }

        descriptor.Name = name;

        if (displayName is not null)
        {
            descriptor.DisplayName = displayName;
        }

        descriptor.DisplayName ??= new LocalizedString(name, name);
        configure?.Invoke(descriptor);

        return this;
    }
}
