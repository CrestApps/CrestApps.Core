namespace CrestApps.Core.AI.Tooling.Parameters;

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
