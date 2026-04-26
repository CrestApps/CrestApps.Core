using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Represents the AI Chat Session Document Authorization Context.
/// </summary>
public sealed class AIChatSessionDocumentAuthorizationContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIChatSessionDocumentAuthorizationContext"/> class.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="session">The session.</param>
    public AIChatSessionDocumentAuthorizationContext(
        AIProfile profile,
        AIChatSession session)
    {
        Profile = profile;
        Session = session;
    }

    /// <summary>
    /// Gets the profile.
    /// </summary>
    public AIProfile Profile { get; }

    /// <summary>
    /// Gets the session.
    /// </summary>
    public AIChatSession Session { get; }
}
