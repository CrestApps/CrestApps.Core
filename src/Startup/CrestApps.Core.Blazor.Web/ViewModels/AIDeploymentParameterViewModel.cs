using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Blazor.Web.ViewModels;

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
    public HashSet<string> SelectedAllowedValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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
    /// Gets or sets the registered descriptor backing this row.
    /// </summary>
    public AIDeploymentParameterDescriptor Descriptor { get; set; }

    /// <summary>
    /// Gets the display text of the registered parameter.
    /// </summary>
    public string DisplayName
        => Descriptor?.DisplayName?.Value ?? Name;

    /// <summary>
    /// Gets the descriptive text of the registered parameter.
    /// </summary>
    public string Description
        => Descriptor?.Description?.Value;

    /// <summary>
    /// Gets the editor semantics of the registered parameter.
    /// </summary>
    public AIDeploymentParameterKind Kind
        => Descriptor?.Kind ?? AIDeploymentParameterKind.Text;

    /// <summary>
    /// Gets the optional trained feature this parameter depends on. When set, the editor only shows the
    /// parameter while the matching feature is enabled.
    /// </summary>
    public string RequiredFeature
        => Descriptor?.RequiredFeature;

    /// <summary>
    /// Gets every value registered for a choice parameter.
    /// </summary>
    public IList<AIDeploymentParameterOption> AvailableValues
        => Descriptor?.AllowedValues ?? [];

    /// <summary>
    /// Gets a slug safe for use inside an element identifier.
    /// </summary>
    public string ElementId
        => Name?.Replace('.', '_');
}
