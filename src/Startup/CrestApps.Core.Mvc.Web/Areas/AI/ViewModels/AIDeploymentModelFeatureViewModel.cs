namespace CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

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
