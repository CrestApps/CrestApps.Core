namespace CrestApps.Core.AI.Security;

/// <summary>
/// Resolves the current visitor identity for AI chat requests.
/// </summary>
public interface IAIVisitorIdentityResolver
{
    /// <summary>
    /// Resolves the current visitor identity for the active request context.
    /// </summary>
    /// <returns>The resolved visitor identity.</returns>
    AIVisitorIdentity Resolve();
}
