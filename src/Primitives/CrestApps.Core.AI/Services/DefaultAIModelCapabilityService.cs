using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default implementation of <see cref="IAIModelCapabilityService"/> that merges the registered
/// model feature and parameter definitions with the metadata stored on a deployment.
/// </summary>
public sealed class DefaultAIModelCapabilityService : IAIModelCapabilityService
{
    private readonly AIModelCapabilityOptions _options;
    private readonly IAIDeploymentStore _deploymentStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIModelCapabilityService"/> class.
    /// </summary>
    /// <param name="options">The registered model capability definitions.</param>
    /// <param name="deploymentStore">The deployment store used to resolve deployments by name.</param>
    public DefaultAIModelCapabilityService(
        IOptions<AIModelCapabilityOptions> options,
        IAIDeploymentStore deploymentStore)
    {
        _options = options.Value;
        _deploymentStore = deploymentStore;
    }

    /// <inheritdoc/>
    public IReadOnlyList<AIModelFeatureDescriptor> GetRegisteredFeatures()
    {
        return [.. _options.Features.Values
            .OrderBy(feature => feature.Order)
            .ThenBy(feature => feature.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <inheritdoc/>
    public IReadOnlyList<AIModelParameterDescriptor> GetRegisteredParameters()
    {
        return [.. _options.Parameters.Values
            .OrderBy(parameter => parameter.Order)
            .ThenBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <inheritdoc/>
    public AIDeploymentCapabilities GetCapabilities(AIDeployment deployment)
    {
        if (deployment is null || !deployment.TryGet<AIDeploymentModelMetadata>(out var metadata))
        {
            return AIDeploymentCapabilities.Empty;
        }

        var features = new List<AIModelFeatureDescriptor>();

        if (metadata.Features is { Length: > 0 })
        {
            foreach (var featureName in metadata.Features)
            {
                if (!string.IsNullOrWhiteSpace(featureName) && _options.Features.TryGetValue(featureName, out var descriptor))
                {
                    features.Add(descriptor);
                }
            }
        }

        var parameters = new List<AIModelParameterDescriptor>();

        if (metadata.Parameters is { Count: > 0 })
        {
            foreach (var (parameterName, overrides) in metadata.Parameters)
            {
                if (string.IsNullOrWhiteSpace(parameterName) || !_options.Parameters.TryGetValue(parameterName, out var descriptor))
                {
                    continue;
                }

                parameters.Add(Merge(descriptor, overrides));
            }
        }

        return new AIDeploymentCapabilities(features, parameters);
    }

    /// <inheritdoc/>
    public async ValueTask<AIDeploymentCapabilities> GetCapabilitiesAsync(string deploymentName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deploymentName))
        {
            return AIDeploymentCapabilities.Empty;
        }

        var deployment = await _deploymentStore.FindByNameAsync(deploymentName, cancellationToken);

        return GetCapabilities(deployment);
    }

    private static AIModelParameterDescriptor Merge(AIModelParameterDescriptor descriptor, AIDeploymentModelParameter overrides)
    {
        var effective = descriptor.Clone();

        if (overrides is null)
        {
            return effective;
        }

        if (overrides.AllowedValues is { Length: > 0 } && effective.AllowedValues is { Count: > 0 })
        {
            effective.AllowedValues =
            [
                .. effective.AllowedValues
                    .Where(option => overrides.AllowedValues.Any(allowed => string.Equals(allowed, option.Value, StringComparison.OrdinalIgnoreCase)))
            ];
        }

        if (overrides.Minimum.HasValue)
        {
            effective.Minimum = overrides.Minimum;
        }

        if (overrides.Maximum.HasValue)
        {
            effective.Maximum = overrides.Maximum;
        }

        if (overrides.Step.HasValue)
        {
            effective.Step = overrides.Step;
        }

        if (!string.IsNullOrWhiteSpace(overrides.DefaultValue))
        {
            effective.DefaultValue = overrides.DefaultValue;
        }

        if (!effective.IsValidValue(effective.DefaultValue))
        {
            effective.DefaultValue = null;
        }

        return effective;
    }
}
