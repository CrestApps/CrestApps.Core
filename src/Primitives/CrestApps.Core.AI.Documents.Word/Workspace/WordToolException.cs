namespace CrestApps.Core.AI.Documents.Word.Workspace;

/// <summary>
/// A Word tool request that cannot be carried out as asked. The message is returned to the model, which can
/// correct the call; nothing the call would have changed is saved.
/// </summary>
internal sealed class WordToolException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WordToolException"/> class.
    /// </summary>
    public WordToolException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WordToolException"/> class.
    /// </summary>
    /// <param name="message">The message the model reads.</param>
    public WordToolException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WordToolException"/> class.
    /// </summary>
    /// <param name="message">The message the model reads.</param>
    /// <param name="innerException">The cause.</param>
    public WordToolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
