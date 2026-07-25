using System.Security.Claims;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Security;

/// <summary>
/// Provides context for prompt security validation.
/// </summary>
public sealed class PromptSecurityContext
{
    /// <summary>
    /// Gets or sets the user prompt text to validate.
    /// </summary>
    public string Prompt { get; set; }

    /// <summary>
    /// Gets or sets the session identifier for the current chat session.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the profile identifier associated with the chat.
    /// </summary>
    public string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the AI profile associated with this validation.
    /// Used to resolve per-profile security settings.
    /// </summary>
    public AIProfile Profile { get; set; }

    /// <summary>
    /// Gets or sets the authenticated user principal.
    /// </summary>
    public ClaimsPrincipal User { get; set; }

    /// <summary>
    /// Gets or sets the connection identifier for the current connection.
    /// </summary>
    public string ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets the resolved visitor identifier for the current request.
    /// </summary>
    public string VisitorId { get; set; }

    /// <summary>
    /// Gets or sets the hashed remote-address signal used only for abuse controls.
    /// </summary>
    public string RemoteAddressHash { get; set; }

    /// <summary>
    /// Gets or sets the captured remote-address value when plain-text or encrypted storage is enabled.
    /// </summary>
    public string RemoteAddress { get; set; }
}
