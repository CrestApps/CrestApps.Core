namespace CrestApps.Core.AI.Exceptions;

public sealed class UnregisteredCompletionClientException : Exception
{
    public UnregisteredCompletionClientException(string clientName)
        : base($"No registered completion client was found to match '{clientName}'.")
    {
    }
}
