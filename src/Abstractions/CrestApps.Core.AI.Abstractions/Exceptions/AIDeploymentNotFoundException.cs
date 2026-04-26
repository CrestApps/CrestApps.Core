namespace CrestApps.Core.AI.Exceptions;

/// <summary>
/// Exception thrown when an AI deployment cannot be found by name or identifier.
/// </summary>
public sealed class AIDeploymentNotFoundException : AIDeploymentConfigurationException
{
    public AIDeploymentNotFoundException(string message)
        : base(message)
    {
    }

    public AIDeploymentNotFoundException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
