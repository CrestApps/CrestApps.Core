namespace CrestApps.Core.AI.Capabilities;

/// <summary>
/// Identifies which set of selected model parameter values applies to a request.
/// </summary>
public enum AIDeploymentParameterScope
{
    /// <summary>
    /// The values selected for the chat deployment.
    /// </summary>
    Chat,

    /// <summary>
    /// The values selected for the utility deployment, which backs background completions such as
    /// title generation, data extraction, and post-session processing.
    /// </summary>
    Utility,
}
