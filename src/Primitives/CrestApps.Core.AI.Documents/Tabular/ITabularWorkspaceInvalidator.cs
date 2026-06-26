namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Invalidates cached in-memory tabular workspaces when their source chat scope changes or is deleted.
/// </summary>
public interface ITabularWorkspaceInvalidator
{
    /// <summary>
    /// Invalidates workspaces associated with the supplied document reference.
    /// </summary>
    /// <param name="referenceType">The document reference type.</param>
    /// <param name="referenceId">The document reference identifier.</param>
    void InvalidateReference(string referenceType, string referenceId);

    /// <summary>
    /// Invalidates workspaces associated with a chat interaction.
    /// </summary>
    /// <param name="chatInteractionId">The chat interaction identifier.</param>
    void InvalidateChatInteraction(string chatInteractionId);

    /// <summary>
    /// Invalidates workspaces associated with a chat session.
    /// </summary>
    /// <param name="chatSessionId">The chat session identifier.</param>
    void InvalidateChatSession(string chatSessionId);

    /// <summary>
    /// Invalidates workspaces associated with an AI profile.
    /// </summary>
    /// <param name="profileId">The profile identifier.</param>
    void InvalidateProfile(string profileId);
}
