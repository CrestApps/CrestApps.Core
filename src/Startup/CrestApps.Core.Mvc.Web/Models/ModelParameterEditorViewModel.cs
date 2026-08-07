using System.Text.Json;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Mvc.Web.Models;

/// <summary>
/// Backs the metadata-driven model parameter editor rendered by the <c>_ModelParameters</c> partial.
/// </summary>
public sealed class ModelParameterEditorViewModel
{
    /// <summary>
    /// Gets or sets the name of the form field that holds the selected chat deployment.
    /// </summary>
    public string DeploymentFieldName { get; set; } = "ChatDeploymentName";

    /// <summary>
    /// Gets or sets the form field prefix used when posting the selected parameter values.
    /// </summary>
    public string FieldPrefix { get; set; } = "ModelParameters";

    /// <summary>
    /// Gets or sets the prefix applied to generated element identifiers so a page can render
    /// more than one editor without colliding.
    /// </summary>
    public string ElementPrefix { get; set; } = "modelParameters";

    /// <summary>
    /// Gets or sets every registered model parameter along with the value currently selected.
    /// </summary>
    public List<ModelParameterFieldViewModel> Parameters { get; set; } = [];

    /// <summary>
    /// Gets or sets the per-deployment capability map serialized as JSON and consumed by the editor script.
    /// </summary>
    public string CapabilitiesJson { get; set; } = "{}";

    /// <summary>
    /// Gets or sets the per-deployment trained feature map serialized as JSON and consumed by the editor
    /// script to render the read-only capability badges. Keyed by deployment name, each value is the list
    /// of trained feature display names the deployment declares.
    /// </summary>
    public string FeaturesJson { get; set; } = "{}";

    /// <summary>
    /// Gets a value indicating whether at least one parameter is registered.
    /// </summary>
    public bool HasParameters
        => Parameters.Count > 0;
}

/// <summary>
/// Represents a single registered model parameter rendered by the editor.
/// </summary>
public sealed class ModelParameterFieldViewModel
{
    /// <summary>
    /// Gets or sets the registered technical name of the parameter.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the display text shown to operators.
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the descriptive text shown to operators.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the editor semantics of the parameter.
    /// </summary>
    public AIModelParameterKind Kind { get; set; }

    /// <summary>
    /// Gets or sets every value registered for a choice parameter.
    /// </summary>
    public List<ModelParameterOptionViewModel> AllowedValues { get; set; } = [];

    /// <summary>
    /// Gets or sets the value currently selected.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Gets a slug safe for use inside an element identifier.
    /// </summary>
    public string ElementId
        => Name?.Replace('.', '_');
}

/// <summary>
/// Represents a selectable value of a choice parameter.
/// </summary>
public sealed class ModelParameterOptionViewModel
{
    /// <summary>
    /// Gets or sets the technical value posted by the editor.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Gets or sets the display text shown to operators.
    /// </summary>
    public string DisplayName { get; set; }
}

/// <summary>
/// Describes the effective metadata of a single parameter for one deployment. The shape of this type
/// matches the JSON consumed by the editor script.
/// </summary>
public sealed class ModelParameterCapabilityViewModel
{
    /// <summary>
    /// Gets or sets the values supported by the deployment, or <see langword="null"/> when every
    /// registered value is supported.
    /// </summary>
    public string[] AllowedValues { get; set; }

    /// <summary>
    /// Gets or sets the value applied when the operator does not select one.
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
    /// Gets the serializer options used when the capability map is written for the editor script.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new(JsonSerializerDefaults.Web);
}
