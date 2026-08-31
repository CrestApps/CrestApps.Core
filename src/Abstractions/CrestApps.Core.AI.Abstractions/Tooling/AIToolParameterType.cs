namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// The JSON schema type of a user-declared <see cref="AIToolInstanceParameter"/>. The value determines
/// both the <c>type</c> emitted into the function schema the AI model sees and the coercion applied to
/// the value supplied at invocation time.
/// </summary>
public enum AIToolParameterType
{
    /// <summary>
    /// A JSON string.
    /// </summary>
    String,

    /// <summary>
    /// A whole number. Values arriving as strings or fractional numbers are coerced when lossless.
    /// </summary>
    Integer,

    /// <summary>
    /// A floating point number.
    /// </summary>
    Number,

    /// <summary>
    /// A boolean. The strings <c>true</c>/<c>false</c> are coerced.
    /// </summary>
    Boolean,

    /// <summary>
    /// A JSON array. No element type is enforced.
    /// </summary>
    Array,

    /// <summary>
    /// A free-form JSON object.
    /// </summary>
    Object,
}
