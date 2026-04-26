namespace CrestApps.Core.AI.Exceptions;

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
