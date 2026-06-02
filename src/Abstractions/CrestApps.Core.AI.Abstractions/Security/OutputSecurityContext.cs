namespace CrestApps.Core.AI.Security;

/// <summary>
/// Provides context for output security filtering.
/// </summary>
public sealed class OutputSecurityContext
{
    /// <summary>
    /// Gets or sets the AI-generated output text to validate.
    /// </summary>
    public string Output { get; set; }

    /// <summary>
    /// Gets or sets the original user prompt that triggered the output.
    /// </summary>
    public string OriginalPrompt { get; set; }

    /// <summary>
    /// Gets or sets the session identifier for the current chat session.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the system message that was provided to the model,
    /// used for leak detection.
    /// </summary>
    public string SystemMessage { get; set; }
}
