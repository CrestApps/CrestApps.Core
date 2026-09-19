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
    /// Gets or sets the heading rendered above the editor. The heading is hidden along with the rest
    /// of the editor when the selected deployment exposes no configurable parameter.
    /// </summary>
    public string Title { get; set; } = "Model parameters";

    /// <summary>
    /// Gets or sets every registered model parameter along with the value currently selected.
    /// </summary>
    public List<ModelParameterFieldViewModel> Parameters { get; set; } = [];

    /// <summary>
    /// Gets or sets the per-deployment capability map serialized as JSON and consumed by the editor script.
    /// </summary>
    public string CapabilitiesJson { get; set; } = "{}";

    /// <summary>
    /// Gets a value indicating whether at least one parameter is registered.
    /// </summary>
    public bool HasParameters
        => Parameters.Count > 0;
}
