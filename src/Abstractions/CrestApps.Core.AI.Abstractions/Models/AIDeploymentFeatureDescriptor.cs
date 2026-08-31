using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Describes a binary capability that a model behind an <see cref="AIDeployment"/> can perform,
/// such as tool calling, structured outputs, or audio input.
/// </summary>
/// <remarks>
/// Features are registered by modules through <see cref="AIDeploymentCapabilityOptions"/> so that
/// providers can contribute new capabilities without changing the core framework.
/// Unlike <see cref="AIDeploymentPurpose"/>, which drives deployment routing, features describe
/// what the underlying model is able to do.
/// </remarks>
public sealed class AIDeploymentFeatureDescriptor
{
    /// <summary>
    /// Gets or sets the technical name that uniquely identifies the feature.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the display text shown to operators.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the descriptive text shown to operators.
    /// </summary>
    public LocalizedString Description { get; set; }

    /// <summary>
    /// Gets or sets the optional grouping category used to organize features in editors.
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    /// Gets or sets the sort order used when features are listed. Lower values are listed first.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the feature is selected by default when a new
    /// deployment is created. Existing deployments are unaffected by this value.
    /// </summary>
    public bool EnabledByDefault { get; set; }
}
