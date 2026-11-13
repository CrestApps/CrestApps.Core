using System.Security.Claims;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Security;

namespace CrestApps.Core.AI.Documents.Endpoints;

/// <summary>
/// Decides whether the caller owns a chat session, for the endpoints that read or change a session's documents.
/// Authorizing the operation on the profile says the caller may manage documents in general; this says the session is
/// theirs. Knowing a session's id is not enough.
/// </summary>
internal static class ChatSessionOwnership
{
    /// <summary>
    /// Determines whether the caller owns the session: the signed-in user who started it, or, for a visitor's session, the
    /// same visitor. The caller is matched on their name identifier; only a caller who has none is matched on their name.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="user">The caller.</param>
    /// <param name="visitorIdentityResolver">The resolver of the anonymous visitor, if the host registered one.</param>
    /// <returns><see langword="true"/> when the caller owns the session.</returns>
    public static bool IsOwner(AIChatSession session, ClaimsPrincipal user, IAIVisitorIdentityResolver visitorIdentityResolver)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!string.IsNullOrEmpty(session.UserId))
        {
            if (user?.Identity?.IsAuthenticated != true)
            {
                return false;
            }

            // The name is only compared for a caller who has no id: where users choose their own names, a user named after another
            // user's id would otherwise own that user's sessions.
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

            return string.Equals(session.UserId, userId ?? user.Identity.Name, StringComparison.Ordinal);
        }

        if (!string.IsNullOrEmpty(session.ClientId))
        {
            return string.Equals(session.ClientId, visitorIdentityResolver?.Resolve()?.VisitorId, StringComparison.Ordinal);
        }

        // A session with no owner belongs to nobody who can prove it.
        return false;
    }
}
