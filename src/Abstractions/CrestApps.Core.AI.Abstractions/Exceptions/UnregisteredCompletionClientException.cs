namespace CrestApps.Core.AI.Exceptions;

/// <summary>
/// Exception thrown when no registered AI completion client can be found for the specified client name.
/// </summary>
public sealed class UnregisteredCompletionClientException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnregisteredCompletionClientException"/> class.
    /// </summary>
    /// <param name="clientName">The client name.</param>
    public UnregisteredCompletionClientException(string clientName)
        : base($"No registered completion client was found to match '{clientName}'.")
    {
    }
}
