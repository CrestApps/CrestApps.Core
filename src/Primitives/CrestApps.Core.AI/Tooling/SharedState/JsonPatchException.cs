namespace CrestApps.Core.AI.Tooling.SharedState;

/// <summary>
/// The exception thrown when a JSON Patch operation cannot be applied.
/// </summary>
public sealed class JsonPatchException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonPatchException"/> class.
    /// </summary>
    public JsonPatchException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonPatchException"/> class.
    /// </summary>
    /// <param name="message">What is wrong with the operation.</param>
    public JsonPatchException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonPatchException"/> class.
    /// </summary>
    /// <param name="message">What is wrong with the operation.</param>
    /// <param name="innerException">The exception that caused it.</param>
    public JsonPatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
