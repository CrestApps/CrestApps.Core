namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Determines who supplies the value of an <see cref="AIToolInstanceParameter"/> at invocation time.
/// Only <see cref="Model"/> parameters are ever declared in the JSON schema the AI model sees; the other
/// modes are resolved entirely server-side so the model can neither read nor influence them.
/// </summary>
public enum AIToolParameterFill
{
    /// <summary>
    /// The AI model supplies the value. The parameter is declared in the function schema; when the model
    /// omits it, the configured default is applied.
    /// </summary>
    Model,

    /// <summary>
    /// The value is pinned by the user that configured the instance and is never exposed to the model.
    /// </summary>
    Fixed,

    /// <summary>
    /// The value is resolved at invocation time from the ambient request context (for example the current
    /// user's identifier) and is never exposed to the model.
    /// </summary>
    Context,
}
