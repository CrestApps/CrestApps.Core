namespace CrestApps.Core.AI.Tooling.Parameters;

/// <summary>
/// Describes a context key an <see cref="IAIToolParameterContextResolver"/> can resolve, so the
/// management UI can offer the available keys as a list instead of asking users to type an identifier
/// they have no way to discover.
/// </summary>
/// <param name="Key">The key stored in <see cref="AIToolInstanceParameter.ContextKey"/>.</param>
/// <param name="DisplayName">The friendly name shown in the management UI.</param>
/// <param name="Description">An optional explanation of what the key resolves to.</param>
public readonly record struct AIToolParameterContextKey(string Key, string DisplayName, string Description = null);

/// <summary>
/// The context keys resolved by the framework's built-in
/// <see cref="DefaultAIToolParameterContextResolver"/>.
/// </summary>
public static class AIToolParameterContextKeys
{
    /// <summary>
    /// The current user's identifier.
    /// </summary>
    public const string UserId = "user.id";

    /// <summary>
    /// The current user's display name.
    /// </summary>
    public const string UserName = "user.name";

    /// <summary>
    /// The current user's email address.
    /// </summary>
    public const string UserEmail = "user.email";

    /// <summary>
    /// The identifier of the resource driving the completion, such as the chat interaction or AI profile.
    /// </summary>
    public const string ResourceId = "resource.id";

    /// <summary>
    /// The current UTC time, in round-trip format.
    /// </summary>
    public const string UtcNow = "now.utc";
}
