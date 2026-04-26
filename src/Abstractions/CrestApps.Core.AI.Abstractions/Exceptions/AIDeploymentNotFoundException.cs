namespace CrestApps.Core.AI.Exceptions;

/// <summary>
/// Exception thrown when an AI deployment cannot be found by name or identifier.
/// </summary>
public sealed class AIDeploymentNotFoundException : AIDeploymentConfigurationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIDeploymentNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public AIDeploymentNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AIDeploymentNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public AIDeploymentNotFoundException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
