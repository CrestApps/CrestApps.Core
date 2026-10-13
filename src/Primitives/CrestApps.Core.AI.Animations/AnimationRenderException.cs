namespace CrestApps.Core.AI.Animations;

/// <summary>
/// The exception thrown when a video cannot be made, with a message the model can pass on.
/// </summary>
public sealed class AnimationRenderException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationRenderException"/> class.
    /// </summary>
    public AnimationRenderException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationRenderException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public AnimationRenderException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimationRenderException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The exception that caused it.</param>
    public AnimationRenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
