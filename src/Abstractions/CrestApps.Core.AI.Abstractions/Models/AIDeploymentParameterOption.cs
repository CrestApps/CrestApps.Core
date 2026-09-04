using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents a single allowed value of an <see cref="AIDeploymentParameterDescriptor"/> whose
/// <see cref="AIDeploymentParameterDescriptor.Kind"/> is <see cref="AIDeploymentParameterKind.Choice"/>.
/// </summary>
public sealed class AIDeploymentParameterOption
{
    /// <summary>
    /// Gets or sets the technical value persisted on the model and sent to the provider.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Gets or sets the display text shown to operators.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the optional descriptive text shown to operators.
    /// </summary>
    public LocalizedString Description { get; set; }
}
