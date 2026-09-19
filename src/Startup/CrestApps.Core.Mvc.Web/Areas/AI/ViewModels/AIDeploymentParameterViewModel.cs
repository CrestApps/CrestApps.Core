using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

/// <summary>
/// Represents the per-deployment settings of a single registered model parameter.
/// </summary>
public sealed class AIDeploymentParameterViewModel
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
    public AIDeploymentParameterKind Kind { get; set; }

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
