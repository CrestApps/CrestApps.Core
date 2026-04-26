namespace CrestApps.Core.AI.Exceptions;

/// <summary>
/// Exception thrown when an AI deployment is misconfigured or cannot be resolved.
/// </summary>
public class AIDeploymentConfigurationException : Exception
{
    public AIDeploymentConfigurationException(string message)
        : base(message)
    {
    }

    public AIDeploymentConfigurationException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
