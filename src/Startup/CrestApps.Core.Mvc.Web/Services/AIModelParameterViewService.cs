using System.Text.Json;
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Mvc.Web.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.Mvc.Web.Services;

/// <summary>
/// Builds the metadata-driven model parameter editor from the registered parameter definitions and
/// the metadata declared by each AI deployment.
/// </summary>
public sealed class AIModelParameterViewService
{
    private readonly IAIModelCapabilityService _capabilityService;
    private readonly ICatalog<AIDeployment> _deploymentCatalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIModelParameterViewService"/> class.
    /// </summary>
    /// <param name="capabilityService">The capability service.</param>
    /// <param name="deploymentCatalog">The deployment catalog.</param>
    public AIModelParameterViewService(
        IAIModelCapabilityService capabilityService,
        ICatalog<AIDeployment> deploymentCatalog)
    {
        _capabilityService = capabilityService;
        _deploymentCatalog = deploymentCatalog;
    }

    /// <summary>
    /// Builds the editor model for the given selected values.
    /// </summary>
    /// <param name="values">The values currently selected, keyed by parameter technical name.</param>
    /// <param name="deploymentFieldName">The name of the form field that holds the selected chat deployment.</param>
    /// <param name="fieldPrefix">The form field prefix used when posting the selected values.</param>
    /// <param name="elementPrefix">The prefix applied to generated element identifiers.</param>
    public async Task<ModelParameterEditorViewModel> BuildAsync(
        IDictionary<string, string> values,
        string deploymentFieldName = "ChatDeploymentName",
        string fieldPrefix = "ModelParameters",
        string elementPrefix = "modelParameters")
    {
        var model = new ModelParameterEditorViewModel
        {
            DeploymentFieldName = deploymentFieldName,
            FieldPrefix = fieldPrefix,
            ElementPrefix = elementPrefix,
        };

        foreach (var descriptor in _capabilityService.GetRegisteredParameters())
        {
            model.Parameters.Add(new ModelParameterFieldViewModel
            {
                Name = descriptor.Name,
                DisplayName = descriptor.DisplayName?.Value ?? descriptor.Name,
                Description = descriptor.Description?.Value,
                Kind = descriptor.Kind,
                Value = values is not null && values.TryGetValue(descriptor.Name, out var value) ? value : null,
                AllowedValues = [.. descriptor.AllowedValues.Select(option => new ModelParameterOptionViewModel
                {
                    Value = option.Value,
                    DisplayName = option.DisplayName?.Value ?? option.Value,
                })],
            });
        }

        model.CapabilitiesJson = JsonSerializer.Serialize(await BuildCapabilityMapAsync(), ModelParameterCapabilityViewModel.SerializerOptions);

        return model;
    }

    private async Task<Dictionary<string, Dictionary<string, ModelParameterCapabilityViewModel>>> BuildCapabilityMapAsync()
    {
        var map = new Dictionary<string, Dictionary<string, ModelParameterCapabilityViewModel>>(StringComparer.OrdinalIgnoreCase);
        var deployments = await _deploymentCatalog.GetAllAsync();

        foreach (var deployment in deployments)
        {
            if (string.IsNullOrWhiteSpace(deployment.Name))
            {
                continue;
            }

            var capabilities = _capabilityService.GetCapabilities(deployment);

            if (capabilities.Parameters.Count == 0)
            {
                continue;
            }

            var entries = new Dictionary<string, ModelParameterCapabilityViewModel>(StringComparer.OrdinalIgnoreCase);

            foreach (var parameter in capabilities.Parameters)
            {
                entries[parameter.Name] = new ModelParameterCapabilityViewModel
                {
                    AllowedValues = parameter.AllowedValues is { Count: > 0 }
                        ? [.. parameter.AllowedValues.Select(option => option.Value)]
                        : null,
                    DefaultValue = parameter.DefaultValue,
                    Minimum = parameter.Minimum,
                    Maximum = parameter.Maximum,
                    Step = parameter.Step,
                };
            }

            map[deployment.Name] = entries;
        }

        return map;
    }
}
