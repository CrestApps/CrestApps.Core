using System.Globalization;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Describes a configurable model parameter, such as reasoning effort or temperature, along with the
/// metadata required to render an editor for it and to validate the value supplied by an operator.
/// </summary>
/// <remarks>
/// Parameters are registered by modules through <see cref="AIModelCapabilityOptions"/>. An
/// <see cref="AIDeployment"/> declares which registered parameters its underlying model exposes and
/// may narrow the registered metadata through <see cref="AIDeploymentModelParameter"/>.
/// </remarks>
public sealed class AIModelParameterDescriptor
{
    /// <summary>
    /// Gets or sets the technical name that uniquely identifies the parameter.
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
    /// Gets or sets the editor and validation semantics of the parameter.
    /// </summary>
    public AIModelParameterKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the allowed values when <see cref="Kind"/> is <see cref="AIModelParameterKind.Choice"/>.
    /// </summary>
    public IList<AIModelParameterOption> AllowedValues { get; set; } = [];

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
    /// Gets or sets the value applied when an operator does not select one.
    /// </summary>
    public string DefaultValue { get; set; }

    /// <summary>
    /// Gets or sets the optional grouping category used to organize parameters in editors.
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    /// Gets or sets the optional name of a trained feature this parameter depends on. When set, the
    /// parameter is only meaningful for deployments that declare the matching
    /// <see cref="AIModelFeatureDescriptor"/>, and editors should hide it unless that feature is enabled.
    /// </summary>
    public string RequiredFeature { get; set; }

    /// <summary>
    /// Gets or sets the sort order used when parameters are listed. Lower values are listed first.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Creates a copy of the descriptor so per-deployment metadata can be applied without
    /// mutating the globally registered definition.
    /// </summary>
    public AIModelParameterDescriptor Clone()
    {
        return new AIModelParameterDescriptor
        {
            Name = Name,
            DisplayName = DisplayName,
            Description = Description,
            Kind = Kind,
            AllowedValues = AllowedValues is null
                ? []
                : [.. AllowedValues],
            Minimum = Minimum,
            Maximum = Maximum,
            Step = Step,
            DefaultValue = DefaultValue,
            Category = Category,
            RequiredFeature = RequiredFeature,
            Order = Order,
        };
    }

    /// <summary>
    /// Determines whether the given value is valid for this parameter.
    /// </summary>
    /// <param name="value">The value to validate. A <see langword="null"/> or empty value is always considered valid.</param>
    public bool IsValidValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        switch (Kind)
        {
            case AIModelParameterKind.Choice:
                return AllowedValues is not { Count: > 0 } ||
                    AllowedValues.Any(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase));

            case AIModelParameterKind.Boolean:
                return bool.TryParse(value, out _);

            case AIModelParameterKind.Integer:
            case AIModelParameterKind.Number:
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                {
                    return false;
                }

                if (Kind == AIModelParameterKind.Integer && number != Math.Truncate(number))
                {
                    return false;
                }

                return (!Minimum.HasValue || number >= Minimum.Value) &&
                    (!Maximum.HasValue || number <= Maximum.Value);

            default:
                return true;
        }
    }
}
