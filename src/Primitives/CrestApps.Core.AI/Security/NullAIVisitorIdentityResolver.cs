namespace CrestApps.Core.AI.Security;

/// <summary>
/// Fallback visitor identity resolver used when a host has not enabled AI chat visitor tracking.
/// </summary>
public sealed class NullAIVisitorIdentityResolver : IAIVisitorIdentityResolver
{
    /// <summary>
    /// Resolves the current visitor identity for the active request context.
    /// </summary>
    /// <returns>An empty visitor identity.</returns>
    public AIVisitorIdentity Resolve()
    {
        return new AIVisitorIdentity();
    }
}
