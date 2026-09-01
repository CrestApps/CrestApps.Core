using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default implementation of <see cref="IAIDeploymentCapabilityService"/> that merges the registered
/// model feature and parameter definitions with the metadata stored on a deployment.
/// </summary>
public sealed class DefaultAIDeploymentCapabilityService : IAIDeploymentCapabilityService
{
    private readonly AIDeploymentCapabilityOptions _options;
    private readonly IAIDeploymentStore _deploymentStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIDeploymentCapabilityService"/> class.
    /// </summary>
    /// <param name="options">The registered model capability definitions.</param>
    /// <param name="deploymentStore">The deployment store used to resolve deployments by name.</param>
    public DefaultAIDeploymentCapabilityService(
        IOptions<AIDeploymentCapabilityOptions> options,
        IAIDeploymentStore deploymentStore)
    {
        _options = options.Value;
        _deploymentStore = deploymentStore;
    }

    /// <inheritdoc/>
    public IReadOnlyList<AIDeploymentFeatureDescriptor> GetRegisteredFeatures()
    {
        return [.. _options.Features.Values
            .OrderBy(feature => feature.Order)
            .ThenBy(feature => feature.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <inheritdoc/>
    public IReadOnlyList<AIDeploymentParameterDescriptor> GetRegisteredParameters()
    {
        return [.. _options.Parameters.Values
            .OrderBy(parameter => parameter.Order)
            .ThenBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <inheritdoc/>
    public AIDeploymentCapabilities GetCapabilities(AIDeployment deployment)
    {
        if (deployment is null || !deployment.TryGet<AIDeploymentMetadata>(out var metadata))
        {
            return AIDeploymentCapabilities.Empty;
        }

        var features = new List<AIDeploymentFeatureDescriptor>();
        var declaredFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (metadata.Features is { Length: > 0 })
        {
            foreach (var featureName in metadata.Features)
            {
                if (string.IsNullOrWhiteSpace(featureName))
                {
                    continue;
                }

                declaredFeatures.Add(featureName);

                if (_options.Features.TryGetValue(featureName, out var descriptor))
                {
                    features.Add(descriptor);
                }
            }
        }

        var parameters = new List<AIDeploymentParameterDescriptor>();

        if (metadata.Parameters is { Count: > 0 })
        {
            foreach (var (parameterName, overrides) in metadata.Parameters)
            {
                if (string.IsNullOrWhiteSpace(parameterName) || !_options.Parameters.TryGetValue(parameterName, out var descriptor))
                {
                    continue;
                }

                // A parameter that depends on a trained feature is only exposed when the deployment
                // declares that feature. This guarantees, at the framework level, that dependent
                // parameters (for example reasoningEffort) are never applied to a model that lacks the
                // capability, regardless of how the metadata was authored.
                if (!string.IsNullOrWhiteSpace(descriptor.RequiredFeature) && !declaredFeatures.Contains(descriptor.RequiredFeature))
                {
                    continue;
                }

                parameters.Add(Merge(descriptor, overrides));
            }
        }

        return new AIDeploymentCapabilities(features, parameters);
    }

    /// <inheritdoc/>
    public bool SupportsFeatureOrUnconstrained(AIDeployment deployment, string featureName)
    {
        if (deployment is null || string.IsNullOrWhiteSpace(featureName))
        {
            return true;
        }

        // A deployment that declares no capability metadata is unconstrained, so it is assumed to support
        // the feature (backward compatible). Once metadata is declared, the feature must be listed.
        if (!deployment.TryGet<AIDeploymentMetadata>(out _))
        {
            return true;
        }

        return GetCapabilities(deployment).SupportsFeature(featureName);
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

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<AIDeployment>> GetDeploymentsWithFeatureAsync(string featureName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(featureName))
        {
            return [];
        }

        var deployments = await _deploymentStore.GetAllAsync(cancellationToken);

        return [.. deployments.Where(deployment => GetCapabilities(deployment).SupportsFeature(featureName))];
    }

    /// <inheritdoc/>
    public async ValueTask<AIDeployment> ResolveDeploymentWithFeatureAsync(string featureName, string deploymentName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(featureName))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(deploymentName))
        {
            var named = await _deploymentStore.FindByNameAsync(deploymentName, cancellationToken);

            return named is not null && GetCapabilities(named).SupportsFeature(featureName)
                ? named
                : null;
        }

        var deployments = await GetDeploymentsWithFeatureAsync(featureName, cancellationToken);

        return deployments.Count > 0 ? deployments[0] : null;
    }

    private static AIDeploymentParameterDescriptor Merge(AIDeploymentParameterDescriptor descriptor, AIDeploymentParameter overrides)
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
