namespace CrestApps.Core.AI.Claude.Models;

/// <summary>
/// Represents an Anthropic model available to the configured API key.
/// </summary>
public sealed class ClaudeModelInfo
{
    /// <summary>
    /// The Anthropic model identifier.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The user-facing model name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The maximum supported input token count when reported by the API.
    /// </summary>
    public long? MaxInputTokens { get; set; }

    /// <summary>
    /// The maximum supported output token count when reported by the API.
    /// </summary>
    public long? MaxOutputTokens { get; set; }
}
