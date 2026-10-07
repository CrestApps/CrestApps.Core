namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// Thrown when an edit cannot be applied, such as a slide number past the end of the deck. The message is
/// written for the model to read and correct its request.
/// </summary>
public sealed class PresentationEditException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationEditException"/> class.
    /// </summary>
    public PresentationEditException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationEditException"/> class.
    /// </summary>
    /// <param name="message">The explanation for the model.</param>
    public PresentationEditException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationEditException"/> class.
    /// </summary>
    /// <param name="message">The explanation for the model.</param>
    /// <param name="innerException">The underlying failure.</param>
    public PresentationEditException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
