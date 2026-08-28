namespace CrestApps.Core.AI.Tooling.Parameters;

/// <summary>
/// Resolves the value of a <see cref="AIToolParameterFill.Context"/> parameter from the ambient request
/// context. Context parameters are the reason a tool instance can safely call a per-user API: the value
/// is injected server-side and never appears in the schema, so a prompt-injected model cannot substitute
/// somebody else's identifier.
/// </summary>
/// <remarks>
/// Resolvers are registered as an ordered enumerable. The first resolver that handles a key wins, so an
/// application can override a built-in key by registering its own resolver ahead of the defaults.
/// </remarks>
public interface IAIToolParameterContextResolver
{
    /// <summary>
    /// Gets the well-known keys this resolver can resolve, used to populate the management UI.
    /// </summary>
    IReadOnlyList<AIToolParameterContextKey> SupportedKeys { get; }

    /// <summary>
    /// Attempts to resolve the value for the supplied context key.
    /// </summary>
    /// <param name="contextKey">The context key configured on the parameter, for example <c>user.id</c>.</param>
    /// <param name="services">The request services available at invocation time.</param>
    /// <param name="value">The resolved value when the key is handled and a value is available.</param>
    /// <returns>
    /// <see langword="true"/> when this resolver owns the key and produced a value. Returning
    /// <see langword="false"/> lets the next resolver try; a key that no resolver handles produces a tool
    /// error rather than a silently missing value.
    /// </returns>
    bool TryResolve(string contextKey, IServiceProvider services, out object value);
}
