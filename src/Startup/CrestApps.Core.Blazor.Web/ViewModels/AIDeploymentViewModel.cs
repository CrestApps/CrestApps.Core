using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class AIDeploymentViewModel
{
    private static readonly HashSet<string> _standaloneProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "AzureSpeech",
    };

    public string ItemId { get; set; }

    public string ModelName { get; set; }

    public string TechnicalName { get; set; }

    public string ConnectionName { get; set; }

    public string ClientName { get; set; }

    // Standalone deployment fields (e.g., Azure AI Services).
    public string Endpoint { get; set; }

    public string AuthenticationType { get; set; }

    public string ApiKey { get; set; }

    public bool IsReadOnly { get; set; }

    public List<KeyValuePair<string, string>> Connections { get; set; } = [];

    public List<KeyValuePair<string, string>> Providers { get; set; } = [];

    public List<KeyValuePair<string, string>> AuthenticationTypes { get; set; } = [];

    /// <summary>
    /// Gets or sets the technical names of the registered model features exposed by this deployment.
    /// </summary>
    public HashSet<string> SelectedFeatures { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets a value indicating whether the deployment carries capability metadata at all.
    /// Feature enforcement constrains a deployment as soon as it does, so a listing cannot say what a
    /// request loses without knowing this.
    /// </summary>
    public bool DeclaresCapabilityMetadata { get; set; }

    /// <summary>
    /// Gets or sets the per-deployment settings of every registered model parameter.
    /// </summary>
    public List<AIDeploymentParameterViewModel> ModelParameters { get; set; } = [];

    /// <summary>
    /// Gets or sets every registered model feature.
    /// </summary>
    public List<AIDeploymentFeatureDescriptor> AvailableFeatures { get; set; } = [];

    public static AIDeploymentViewModel FromDeployment(AIDeployment deployment)
    {
        var model = new AIDeploymentViewModel
        {
            ItemId = deployment.ItemId,
            ModelName = deployment.ModelName,
            TechnicalName = deployment.Name,
            ConnectionName = deployment.ConnectionName,
            ClientName = AIProviderNameNormalizer.Normalize(deployment.ClientName),
            IsReadOnly = deployment.IsReadOnly,
        };

        if (deployment.Properties != null)
        {
            model.Endpoint = deployment.Properties.TryGetValue("Endpoint", out var ep) ? ep?.ToString() : null;
            model.AuthenticationType = deployment.Properties.TryGetValue("AuthenticationType", out var auth) ? auth?.ToString() : null;

            // Read the same way enforcement does. An empty feature list and no metadata at all look
            // alike once the features are copied out, but they mean opposite things to the runtime.
            model.DeclaresCapabilityMetadata = deployment.TryGet<AIDeploymentMetadata>(out _);

            var metadata = deployment.GetOrCreate<AIDeploymentMetadata>();
            model.SelectedFeatures = new HashSet<string>(metadata.Features ?? [], StringComparer.OrdinalIgnoreCase);

            if (metadata.Parameters is { Count: > 0 })
            {
                model.ModelParameters =
                [
                    .. metadata.Parameters.Select(entry => new AIDeploymentParameterViewModel
                    {
                        Name = entry.Key,
                        IsSupported = true,
                        SelectedAllowedValues = new HashSet<string>(entry.Value?.AllowedValues ?? [], StringComparer.OrdinalIgnoreCase),
                        DefaultValue = entry.Value?.DefaultValue,
                        Minimum = entry.Value?.Minimum,
                        Maximum = entry.Value?.Maximum,
                        Step = entry.Value?.Step,
                    })
                ];
            }
        }

        return model;
    }

    /// <summary>
    /// Gets the chat request capabilities that feature enforcement removes from every request sent to
    /// this deployment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enforcement is often described as opt-in, which is only true of the metadata as a whole: a
    /// deployment is constrained the moment it declares any, so a source that always emits metadata —
    /// the configuration catalog, whose deployments are read-only here — makes enforcement mandatory for
    /// everything it produces. A declaration narrowed to text generation therefore strips every tool
    /// from every request and completes a streaming call in one piece, and until now said so only in a
    /// log warning.
    /// </para>
    /// <para>
    /// Only tool calling and streaming are reported. They silently degrade a deployment that otherwise
    /// works, while an undeclared reasoning or structured-output capability is the ordinary state of
    /// most models and flagging it would bury the signal. A deployment that declares no metadata is
    /// unconstrained and reports nothing, and so does one that does not declare text generation,
    /// because the chat enforcement path never runs for it. Mirrors the MVC deployment listing.
    /// </para>
    /// </remarks>
    public IReadOnlyList<AIDeploymentEnforcedLimitViewModel> GetEnforcedLimits()
    {
        if (!DeclaresCapabilityMetadata || !DeclaresFeature(AIDeploymentFeatureNames.TextGeneration))
        {
            return [];
        }

        var limits = new List<AIDeploymentEnforcedLimitViewModel>();

        if (!DeclaresFeature(AIDeploymentFeatureNames.ToolCalling))
        {
            limits.Add(new AIDeploymentEnforcedLimitViewModel
            {
                FeatureName = AIDeploymentFeatureNames.ToolCalling,
                Label = "Tools removed",
                Description = $"This deployment declares its capabilities but not '{AIDeploymentFeatureNames.ToolCalling}', so every tool is removed from every request sent to it.",
            });
        }

        if (!DeclaresFeature(AIDeploymentFeatureNames.Streaming))
        {
            limits.Add(new AIDeploymentEnforcedLimitViewModel
            {
                FeatureName = AIDeploymentFeatureNames.Streaming,
                Label = "Streaming buffered",
                Description = $"This deployment declares its capabilities but not '{AIDeploymentFeatureNames.Streaming}', so a streaming request is completed as a single response.",
            });
        }

        return limits;
    }

    public void ApplyTo(AIDeployment deployment)
    {
        deployment.Name = TechnicalName;
        deployment.ModelName = ModelName;
        deployment.ConnectionName = ConnectionName;
        deployment.ClientName = AIProviderNameNormalizer.Normalize(ClientName);

        deployment.Properties ??= new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(Endpoint))
        {
            deployment.Properties["Endpoint"] = Endpoint;
        }
        else
        {
            deployment.Properties.Remove("Endpoint");
        }

        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            deployment.Properties["ApiKey"] = ApiKey;
        }

        if (!string.IsNullOrWhiteSpace(AuthenticationType))
        {
            deployment.Properties["AuthenticationType"] = AuthenticationType;
        }
        else
        {
            deployment.Properties.Remove("AuthenticationType");
        }

        ApplyModelMetadataTo(deployment);
    }

    /// <summary>
    /// Merges the registered feature and parameter definitions into the editor so unsaved selections
    /// are preserved while display metadata is refreshed.
    /// </summary>
    /// <param name="features">The registered model features.</param>
    /// <param name="parameters">The registered model parameters.</param>
    public void MergeRegisteredCapabilities(
        IEnumerable<AIDeploymentFeatureDescriptor> features,
        IEnumerable<AIDeploymentParameterDescriptor> parameters)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(parameters);

        AvailableFeatures = [.. features];

        var existing = (ModelParameters ?? [])
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .GroupBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var merged = new List<AIDeploymentParameterViewModel>();

        foreach (var descriptor in parameters)
        {
            if (!existing.TryGetValue(descriptor.Name, out var row))
            {
                row = new AIDeploymentParameterViewModel
                {
                    Name = descriptor.Name,
                };
            }

            row.Descriptor = descriptor;
            row.Minimum ??= descriptor.Minimum;
            row.Maximum ??= descriptor.Maximum;
            row.Step ??= descriptor.Step;
            merged.Add(row);
        }

        ModelParameters = merged;
    }

    /// <summary>
    /// Validates the per-deployment parameter metadata against the registered parameter definitions and
    /// returns a message for each incoherent value so the operator is corrected at save time instead of
    /// relying on the request-time sanitizer to silently drop the value. Mirrors the MVC deployment editor.
    /// </summary>
    public IReadOnlyList<string> ValidateModelParameters()
    {
        var errors = new List<string>();

        foreach (var row in ModelParameters ?? [])
        {
            if (row is null || !row.IsSupported || string.IsNullOrWhiteSpace(row.Name))
            {
                continue;
            }

            var descriptor = row.Descriptor;

            if (descriptor is null)
            {
                errors.Add($"'{row.Name}' is not a registered model parameter.");

                continue;
            }

            var label = row.DisplayName;

            // Build the effective descriptor exactly as the runtime does (registered definition narrowed
            // by the per-deployment overrides), then reuse the descriptor's own validation so the editor
            // and the request pipeline agree on what is valid.
            var effective = descriptor.Clone();

            if (row.SelectedAllowedValues is { Count: > 0 })
            {
                var registeredValues = new HashSet<string>(
                    descriptor.AllowedValues?.Select(option => option.Value) ?? [],
                    StringComparer.OrdinalIgnoreCase);

                foreach (var selected in row.SelectedAllowedValues.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    if (!registeredValues.Contains(selected))
                    {
                        errors.Add($"{label}: '{selected}' is not a registered value.");
                    }
                }

                effective.AllowedValues =
                [
                    .. (descriptor.AllowedValues ?? [])
                        .Where(option => row.SelectedAllowedValues.Contains(option.Value))
                ];
            }

            if (row.Minimum.HasValue)
            {
                effective.Minimum = row.Minimum;
            }

            if (row.Maximum.HasValue)
            {
                effective.Maximum = row.Maximum;
            }

            if (row.Step.HasValue)
            {
                effective.Step = row.Step;
            }

            if (effective.Minimum.HasValue && effective.Maximum.HasValue && effective.Minimum > effective.Maximum)
            {
                errors.Add($"{label}: the minimum cannot be greater than the maximum.");
            }

            if (effective.Step is <= 0)
            {
                errors.Add($"{label}: the step must be greater than zero.");
            }

            if (!string.IsNullOrWhiteSpace(row.DefaultValue) && !effective.IsValidValue(row.DefaultValue))
            {
                errors.Add($"{label}: the default value '{row.DefaultValue}' is not valid for the supported values or range.");
            }
        }

        return errors;
    }

    private void ApplyModelMetadataTo(AIDeployment deployment)
    {
        var metadata = new AIDeploymentMetadata
        {
            Features = SelectedFeatures is null
                ? []
                : [.. SelectedFeatures.Where(static feature => !string.IsNullOrWhiteSpace(feature))],
        };

        foreach (var parameter in ModelParameters ?? [])
        {
            if (!parameter.IsSupported || string.IsNullOrWhiteSpace(parameter.Name))
            {
                continue;
            }

            metadata.Parameters[parameter.Name] = new AIDeploymentParameter
            {
                AllowedValues = parameter.SelectedAllowedValues is { Count: > 0 }
                    ? [.. parameter.SelectedAllowedValues]
                    : null,
                DefaultValue = string.IsNullOrWhiteSpace(parameter.DefaultValue) ? null : parameter.DefaultValue,
                Minimum = parameter.Minimum,
                Maximum = parameter.Maximum,
                Step = parameter.Step,
            };
        }

        if (metadata.Features.Length == 0 && metadata.Parameters.Count == 0)
        {
            deployment.Remove<AIDeploymentMetadata>();

            return;
        }

        deployment.Put(metadata);
    }


    public bool UsesStandaloneProvider()
    {
        return _standaloneProviders.Contains(ClientName ?? string.Empty);
    }

    private bool DeclaresFeature(string featureName)
    {
        return SelectedFeatures is { Count: > 0 } && SelectedFeatures.Contains(featureName);
    }
}
