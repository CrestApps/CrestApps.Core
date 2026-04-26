namespace CrestApps.Core.AI.Copilot;

/// <summary>
/// Protected (encrypted) credential stored per user.
/// </summary>
public sealed class CopilotProtectedCredential
{
    /// <summary>
    /// Gets or sets the git Hub Username.
    /// </summary>
    public string GitHubUsername { get; set; }

    /// <summary>
    /// Gets or sets the protected Access Token.
    /// </summary>
    public string ProtectedAccessToken { get; set; }

    /// <summary>
    /// Gets or sets the protected Refresh Token.
    /// </summary>
    public string ProtectedRefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the expires At.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the updated Utc.
    /// </summary>
    public DateTime? UpdatedUtc { get; set; }
}
