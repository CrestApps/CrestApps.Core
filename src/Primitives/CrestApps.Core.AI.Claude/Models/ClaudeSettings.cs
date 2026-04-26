namespace CrestApps.Core.AI.Claude.Models;

/// <summary>
/// Represents the claude Settings.
/// </summary>
public sealed class ClaudeSettings
{
    /// <summary>
    /// Gets or sets the authentication Type.
    /// </summary>
    public ClaudeAuthenticationType AuthenticationType { get; set; }

    /// <summary>
    /// Gets or sets the base Url.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    /// <summary>
    /// Gets or sets the protected Api Key.
    /// </summary>
    public string ProtectedApiKey { get; set; }

    /// <summary>
    /// Gets or sets the default Model.
    /// </summary>
    public string DefaultModel { get; set; }
}
