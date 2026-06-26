namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Builds the stable workspace key used to scope an in-memory tabular workspace to a single
/// conversation. The key prefers the chat session, falling back to the chat interaction or
/// AI profile, and is shared by the tabular tools and document lifecycle handlers.
/// </summary>
internal static class TabularWorkspaceKey
{
    /// <summary>
    /// Builds the workspace key for a chat session.
    /// </summary>
    /// <param name="sessionId">The chat session identifier.</param>
    /// <returns>The workspace key.</returns>
    public static string ForSession(string sessionId)
    {
        return $"session:{sessionId}";
    }

    /// <summary>
    /// Builds the workspace key for a chat interaction.
    /// </summary>
    /// <param name="interactionId">The chat interaction identifier.</param>
    /// <returns>The workspace key.</returns>
    public static string ForInteraction(string interactionId)
    {
        return $"interaction:{interactionId}";
    }

    /// <summary>
    /// Builds the workspace key for an AI profile.
    /// </summary>
    /// <param name="profileId">The AI profile identifier.</param>
    /// <returns>The workspace key.</returns>
    public static string ForProfile(string profileId)
    {
        return $"profile:{profileId}";
    }
}
