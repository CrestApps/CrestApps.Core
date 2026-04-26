namespace CrestApps.Core.AI.Exceptions;

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
