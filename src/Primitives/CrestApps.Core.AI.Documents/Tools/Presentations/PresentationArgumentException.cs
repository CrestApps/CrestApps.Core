namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Thrown when a tool's arguments cannot be read. The message is written for the model to correct its call.
/// </summary>
internal sealed class PresentationArgumentException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationArgumentException"/> class.
    /// </summary>
    public PresentationArgumentException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationArgumentException"/> class.
    /// </summary>
    /// <param name="message">The explanation for the model.</param>
    public PresentationArgumentException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationArgumentException"/> class.
    /// </summary>
    /// <param name="message">The explanation for the model.</param>
    /// <param name="innerException">The underlying failure.</param>
    public PresentationArgumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
