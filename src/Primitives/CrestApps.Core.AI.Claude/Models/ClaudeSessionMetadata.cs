namespace CrestApps.Core.AI.Claude.Models;

/// <summary>
/// Metadata specific to Anthropic orchestrator sessions.
/// Stored on <see cref="AI.Models.ChatInteraction"/> and <see cref="AI.Models.AIProfile"/>.
/// </summary>
public sealed class ClaudeSessionMetadata
{
    /// <summary>
    /// The Anthropic model override for the session.
    /// </summary>
    public string ClaudeModel { get; set; }
}
