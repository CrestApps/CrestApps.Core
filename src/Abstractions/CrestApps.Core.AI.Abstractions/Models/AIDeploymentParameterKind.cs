namespace CrestApps.Core.AI.Models;

/// <summary>
/// Describes the editor and validation semantics of an <see cref="AIDeploymentParameterDescriptor"/>.
/// The value drives how a parameter is rendered and validated by metadata-driven editors.
/// </summary>
public enum AIDeploymentParameterKind
{
    /// <summary>
    /// The parameter accepts one value from a closed list of allowed values and renders as a dropdown.
    /// </summary>
    Choice = 0,

    /// <summary>
    /// The parameter accepts a floating point number within an optional range and renders as a numeric input.
    /// </summary>
    Number = 1,

    /// <summary>
    /// The parameter accepts a whole number within an optional range and renders as a numeric input.
    /// </summary>
    Integer = 2,

    /// <summary>
    /// The parameter accepts a <see langword="true"/> or <see langword="false"/> value and renders as a checkbox.
    /// </summary>
    Boolean = 3,

    /// <summary>
    /// The parameter accepts free-form text and renders as a text input.
    /// </summary>
    Text = 4,
}
