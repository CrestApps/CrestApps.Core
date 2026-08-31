using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents a single allowed value of an <see cref="AIModelParameterDescriptor"/> whose
/// <see cref="AIModelParameterDescriptor.Kind"/> is <see cref="AIModelParameterKind.Choice"/>.
/// </summary>
public sealed class AIModelParameterOption
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
