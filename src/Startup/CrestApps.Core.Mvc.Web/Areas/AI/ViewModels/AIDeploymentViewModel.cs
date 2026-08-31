using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

/// <summary>
/// Pairs a registered model parameter with its position in the deployment's parameter list so a single
/// parameter card can be rendered in a partial while keeping its form-binding index.
/// </summary>
public sealed class AIDeploymentModelParameterCardViewModel
{
    /// <summary>
    /// Gets or sets the parameter to render.
    /// </summary>
    public AIDeploymentModelParameterViewModel Parameter { get; set; }

    /// <summary>
    /// Gets or sets the parameter's index within <see cref="AIDeploymentViewModel.ModelParameters"/>.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the card should be hidden on initial render because its
    /// required feature is not currently enabled.
    /// </summary>
    public bool Hidden { get; set; }
}

/// <summary>
/// Represents the per-deployment settings of a single registered model parameter.
/// </summary>
public sealed class AIDeploymentModelParameterViewModel
{
    /// <summary>
    /// Gets or sets the registered technical name of the parameter.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the deployment exposes this parameter.
    /// </summary>
    public bool IsSupported { get; set; }

    /// <summary>
    /// Gets or sets the subset of registered values supported by the deployment. An empty selection
    /// means every registered value is supported.
    /// </summary>
    public string[] SelectedAllowedValues { get; set; } = [];

    /// <summary>
    /// Gets or sets the value applied when an operator does not select one.
    /// </summary>
    public string DefaultValue { get; set; }

    /// <summary>
    /// Gets or sets the inclusive minimum accepted value for numeric parameters.
    /// </summary>
    public double? Minimum { get; set; }

    /// <summary>
    /// Gets or sets the inclusive maximum accepted value for numeric parameters.
    /// </summary>
    public double? Maximum { get; set; }

    /// <summary>
    /// Gets or sets the increment applied by numeric editors.
    /// </summary>
    public double? Step { get; set; }

    /// <summary>
    /// Gets or sets the display text of the registered parameter.
    /// </summary>
    [BindNever]
    public string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the descriptive text of the registered parameter.
    /// </summary>
    [BindNever]
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the editor semantics of the registered parameter.
    /// </summary>
    [BindNever]
    public AIModelParameterKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the optional trained feature this parameter depends on. When set, the editor only
    /// shows the parameter while the matching feature is enabled.
    /// </summary>
    [BindNever]
    public string RequiredFeature { get; set; }

    /// <summary>
    /// Gets or sets every value registered for a choice parameter.
    /// </summary>
    [BindNever]
    public IEnumerable<SelectListItem> AvailableValues { get; set; } = [];

    /// <summary>
    /// Gets a slug safe for use inside an element identifier.
    /// </summary>
    [BindNever]
    public string ElementId
        => Name?.Replace('.', '_');
}

/// <summary>
/// Represents a registered model feature that a deployment can expose.
/// </summary>
public sealed class AIDeploymentModelFeatureViewModel
{
    /// <summary>
    /// Gets or sets the registered technical name of the feature.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the display text of the registered feature.
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the descriptive text of the registered feature.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the grouping category of the registered feature.
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the feature is selected by default on new deployments.
    /// </summary>
    public bool EnabledByDefault { get; set; }

    /// <summary>
    /// Gets a slug safe for use inside an element identifier.
    /// </summary>
    public string ElementId
        => Name?.Replace('.', '_');
}

public sealed class AIDeploymentViewModel
{
    private static readonly HashSet<string> _standaloneProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "AzureSpeech",
    };

    public string ItemId { get; set; }

    public string ModelName { get; set; }

    public string TechnicalName { get; set; }
    public string[] SelectedPurposes { get; set; } = [];
    public string ConnectionName { get; set; }

    public string ClientName { get; set; }

    // Standalone deployment fields (e.g., Azure AI Services).
    public string Endpoint { get; set; }

    public string AuthenticationType { get; set; }

    public string ApiKey { get; set; }

    public bool IsReadOnly { get; set; }

    [BindNever]
    public IEnumerable<SelectListItem> Connections { get; set; } = [];

    [BindNever]
    public IEnumerable<SelectListItem> Providers { get; set; } = [];

    [BindNever]
    public IEnumerable<SelectListItem> AuthenticationTypes { get; set; } = [];

    [BindNever]
    public IEnumerable<SelectListItem> Purposes { get; set; } = [];

    /// <summary>
    /// Gets or sets the technical names of the registered model features exposed by this deployment.
    /// </summary>
    public string[] SelectedFeatures { get; set; } = [];

    /// <summary>
    /// Gets or sets the per-deployment settings of every registered model parameter.
    /// </summary>
    public List<AIDeploymentModelParameterViewModel> ModelParameters { get; set; } = [];

    /// <summary>
    /// Gets or sets every registered model feature.
    /// </summary>
    [BindNever]
    public IEnumerable<AIDeploymentModelFeatureViewModel> AvailableFeatures { get; set; } = [];

    public static AIDeploymentViewModel FromDeployment(AIDeployment deployment)
    {
        var model = new AIDeploymentViewModel
        {
            ItemId = deployment.ItemId,
            ModelName = deployment.ModelName,
            TechnicalName = deployment.Name,
            SelectedPurposes = deployment.Purpose.GetSupportedPurposes()
                .Select(static purpose => purpose.ToString())
            .ToArray(),
            ConnectionName = deployment.ConnectionName,
            ClientName = AIProviderNameNormalizer.Normalize(deployment.ClientName),
            IsReadOnly = deployment.IsReadOnly,
        };

        if (deployment.Properties != null)
        {
            model.Endpoint = deployment.Properties.TryGetValue("Endpoint", out var ep) ? ep?.ToString() : null;
            model.AuthenticationType = deployment.Properties.TryGetValue("AuthenticationType", out var auth) ? auth?.ToString() : null;

            var metadata = deployment.GetOrCreate<AIDeploymentModelMetadata>();
            model.SelectedFeatures = metadata.Features ?? [];

            if (metadata.Parameters is { Count: > 0 })
            {
                model.ModelParameters =
                [
                    .. metadata.Parameters.Select(entry => new AIDeploymentModelParameterViewModel
                    {
                        Name = entry.Key,
                        IsSupported = true,
                        SelectedAllowedValues = entry.Value?.AllowedValues ?? [],
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
    /// Merges the registered feature and parameter definitions into the editor so unsaved selections
    /// are preserved while display metadata is refreshed.
    /// </summary>
    /// <param name="features">The registered model features.</param>
    /// <param name="parameters">The registered model parameters.</param>
    public void MergeRegisteredCapabilities(
        IEnumerable<AIModelFeatureDescriptor> features,
        IEnumerable<AIModelParameterDescriptor> parameters)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(parameters);

        AvailableFeatures =
        [
            .. features.Select(feature => new AIDeploymentModelFeatureViewModel
            {
                Name = feature.Name,
                DisplayName = feature.DisplayName?.Value ?? feature.Name,
                Description = feature.Description?.Value,
                Category = feature.Category,
                EnabledByDefault = feature.EnabledByDefault,
            })
        ];

        var existing = (ModelParameters ?? [])
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .GroupBy(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var merged = new List<AIDeploymentModelParameterViewModel>();

        foreach (var descriptor in parameters)
        {
            if (!existing.TryGetValue(descriptor.Name, out var row))
            {
                row = new AIDeploymentModelParameterViewModel
                {
                    Name = descriptor.Name,
                };
            }

            row.DisplayName = descriptor.DisplayName?.Value ?? descriptor.Name;
            row.Description = descriptor.Description?.Value;
            row.Kind = descriptor.Kind;
            row.RequiredFeature = descriptor.RequiredFeature;
            row.AvailableValues =
            [
                .. descriptor.AllowedValues.Select(option => new SelectListItem(option.DisplayName?.Value ?? option.Value, option.Value))
            ];
            row.Minimum ??= descriptor.Minimum;
            row.Maximum ??= descriptor.Maximum;
            row.Step ??= descriptor.Step;
            merged.Add(row);
        }

        ModelParameters = merged;
    }

    /// <summary>
    /// Validates the per-deployment parameter metadata against the registered parameter definitions and
    /// records any incoherent values in <paramref name="modelState"/> so the operator is corrected at
    /// save time instead of relying on the request-time sanitizer to silently drop the value.
    /// </summary>
    /// <param name="registeredParameters">The registered model parameter definitions.</param>
    /// <param name="modelState">The model state that receives the validation errors.</param>
    public void ValidateModelParameters(
        IEnumerable<AIModelParameterDescriptor> registeredParameters,
        ModelStateDictionary modelState)
    {
        ArgumentNullException.ThrowIfNull(registeredParameters);
        ArgumentNullException.ThrowIfNull(modelState);

        if (ModelParameters is not { Count: > 0 })
        {
            return;
        }

        var registered = registeredParameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < ModelParameters.Count; i++)
        {
            var row = ModelParameters[i];

            if (row is null || !row.IsSupported || string.IsNullOrWhiteSpace(row.Name))
            {
                continue;
            }

            var prefix = $"{nameof(ModelParameters)}[{i}]";

            if (!registered.TryGetValue(row.Name, out var descriptor))
            {
                modelState.AddModelError($"{prefix}.{nameof(row.Name)}", $"'{row.Name}' is not a registered model parameter.");

                continue;
            }

            // Build the effective descriptor exactly as the runtime does (registered definition narrowed
            // by the per-deployment overrides), then reuse the descriptor's own validation so the editor
            // and the request pipeline agree on what is valid.
            var effective = descriptor.Clone();

            if (row.SelectedAllowedValues is { Length: > 0 })
            {
                var registeredValues = new HashSet<string>(
                    descriptor.AllowedValues?.Select(option => option.Value) ?? [],
                    StringComparer.OrdinalIgnoreCase);

                foreach (var selected in row.SelectedAllowedValues.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    if (!registeredValues.Contains(selected))
                    {
                        modelState.AddModelError($"{prefix}.{nameof(row.SelectedAllowedValues)}", $"'{selected}' is not a registered value for '{descriptor.Name}'.");
                    }
                }

                effective.AllowedValues =
                [
                    .. (descriptor.AllowedValues ?? [])
                        .Where(option => row.SelectedAllowedValues.Any(value => string.Equals(value, option.Value, StringComparison.OrdinalIgnoreCase)))
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
                modelState.AddModelError($"{prefix}.{nameof(row.Minimum)}", "The minimum cannot be greater than the maximum.");
            }

            if (effective.Step is <= 0)
            {
                modelState.AddModelError($"{prefix}.{nameof(row.Step)}", "The step must be greater than zero.");
            }

            if (!string.IsNullOrWhiteSpace(row.DefaultValue) && !effective.IsValidValue(row.DefaultValue))
            {
                modelState.AddModelError($"{prefix}.{nameof(row.DefaultValue)}", $"The default value '{row.DefaultValue}' is not valid for the supported values or range of '{descriptor.Name}'.");
            }
        }
    }

    public void ApplyTo(AIDeployment deployment)
    {
        deployment.Name = TechnicalName;
        deployment.ModelName = ModelName;
        deployment.Purpose = GetDeploymentPurpose();
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

    private void ApplyModelMetadataTo(AIDeployment deployment)
    {
        var metadata = new AIDeploymentModelMetadata
        {
            Features = SelectedFeatures is null
                ? []
                : [.. SelectedFeatures.Where(static feature => !string.IsNullOrWhiteSpace(feature)).Distinct(StringComparer.OrdinalIgnoreCase)],
        };

        foreach (var parameter in ModelParameters ?? [])
        {
            if (!parameter.IsSupported || string.IsNullOrWhiteSpace(parameter.Name))
            {
                continue;
            }

            metadata.Parameters[parameter.Name] = new AIDeploymentModelParameter
            {
                AllowedValues = parameter.SelectedAllowedValues is { Length: > 0 }
                    ? [.. parameter.SelectedAllowedValues.Where(static value => !string.IsNullOrWhiteSpace(value))]
                    : null,
                DefaultValue = string.IsNullOrWhiteSpace(parameter.DefaultValue) ? null : parameter.DefaultValue,
                Minimum = parameter.Minimum,
                Maximum = parameter.Maximum,
                Step = parameter.Step,
            };
        }

        if (metadata.Features.Length == 0 && metadata.Parameters.Count == 0)
        {
            deployment.Remove<AIDeploymentModelMetadata>();

            return;
        }

        deployment.Put(metadata);
    }

    public AIDeploymentPurpose GetDeploymentPurpose()
    {
        var deploymentPurpose = AIDeploymentPurpose.None;

        if (SelectedPurposes is null)
        {
            return deploymentPurpose;
        }

        foreach (var purposeName in SelectedPurposes.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            if (Enum.TryParse<AIDeploymentPurpose>(purposeName, ignoreCase: true, out var parsedPurpose)
                && parsedPurpose != AIDeploymentPurpose.None)
            {
                deploymentPurpose |= parsedPurpose;
            }
        }

        return deploymentPurpose;
    }

    public bool UsesStandaloneProvider()
    {
        return _standaloneProviders.Contains(ClientName ?? string.Empty);
    }
}
