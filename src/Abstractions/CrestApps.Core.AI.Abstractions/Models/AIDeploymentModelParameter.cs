namespace CrestApps.Core.AI.Models;

/// <summary>
/// Describes the per-deployment metadata of a supported model parameter. Every member is optional and,
/// when supplied, narrows the globally registered <see cref="AIDeploymentParameterDescriptor"/> so a deployment
/// can describe the exact behavior of its underlying model.
/// </summary>
public sealed class AIDeploymentModelParameter
{
    /// <summary>
    /// Gets or sets the subset of allowed values supported by this deployment.
    /// When empty, the registered allowed values are used.
    /// </summary>
    public string[] AllowedValues { get; set; }

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
    /// Creates a copy of this instance.
    /// </summary>
    public AIDeploymentModelParameter Clone()
    {
        return new AIDeploymentModelParameter
        {
            AllowedValues = AllowedValues is null
                ? null
                : [.. AllowedValues],
            DefaultValue = DefaultValue,
            Minimum = Minimum,
            Maximum = Maximum,
            Step = Step,
        };
    }
}
