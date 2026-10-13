namespace CrestApps.Core.AI.Animations.Scripting;

/// <summary>
/// The exception thrown when an animation script fails or breaks a limit, with a message the model can use to
/// fix the script.
/// </summary>
public sealed class AnimationScriptException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationScriptException"/> class.
    /// </summary>
    public AnimationScriptException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationScriptException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public AnimationScriptException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationScriptException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The exception that caused it.</param>
    public AnimationScriptException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
