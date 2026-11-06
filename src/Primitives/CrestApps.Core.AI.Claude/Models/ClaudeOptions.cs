namespace CrestApps.Core.AI.Claude.Models;

/// <summary>
/// Options for configuring the Anthropic orchestrator.
/// </summary>
public sealed class ClaudeOptions
{
    /// <summary>
    /// The Anthropic API key.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// The Anthropic API base URL.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    /// <summary>
    /// The default model identifier used when a session does not override it.
    /// </summary>
    public string DefaultModel { get; set; }

    /// <summary>
    /// Whether requests mark Anthropic prompt-cache breakpoints on the last tool definition, the end of the
    /// system prompt and the latest message, so the repeated prefix is billed at the cache-read rate.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool EnablePromptCaching { get; set; } = true;
}
