using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Mvc.Web.Models;

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
    public AIDeploymentParameterKind Kind { get; set; }

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
