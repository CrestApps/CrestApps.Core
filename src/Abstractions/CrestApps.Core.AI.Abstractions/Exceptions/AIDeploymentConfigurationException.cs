namespace CrestApps.Core.AI.Exceptions;

/// <summary>
/// Exception thrown when an AI deployment is misconfigured or cannot be resolved.
/// </summary>
public class AIDeploymentConfigurationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIDeploymentConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public AIDeploymentConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AIDeploymentConfigurationException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public AIDeploymentConfigurationException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
