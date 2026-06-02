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
}
