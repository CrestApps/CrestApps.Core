namespace CrestApps.Core.AI.Exceptions;

/// <summary>
/// Exception thrown when no registered AI completion client can be found for the specified client name.
/// </summary>
public sealed class UnregisteredCompletionClientException : Exception
{
    public UnregisteredCompletionClientException(string clientName)
        : base($"No registered completion client was found to match '{clientName}'.")
    {
    }
}
