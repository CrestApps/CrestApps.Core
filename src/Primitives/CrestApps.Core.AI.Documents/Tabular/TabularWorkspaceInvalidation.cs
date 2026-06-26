namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Describes a tabular workspace invalidation event that can be published locally or through
/// a distributed backplane.
/// </summary>
public sealed class TabularWorkspaceInvalidation
{
    private TabularWorkspaceInvalidation(string kind, string referenceType, string referenceId)
    {
        Kind = kind;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
    }

    /// <summary>
    /// Gets the invalidation kind.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Gets the document reference type when <see cref="Kind"/> is <see cref="ReferenceKind"/>.
    /// </summary>
    public string ReferenceType { get; }

    /// <summary>
    /// Gets the reference identifier.
    /// </summary>
    public string ReferenceId { get; }

    /// <summary>
    /// Gets the invalidation kind for document references.
    /// </summary>
    public const string ReferenceKind = "reference";

    /// <summary>
    /// Gets the invalidation kind for chat interactions.
    /// </summary>
    public const string ChatInteractionKind = "chat-interaction";

    /// <summary>
    /// Gets the invalidation kind for chat sessions.
    /// </summary>
    public const string ChatSessionKind = "chat-session";

    /// <summary>
    /// Gets the invalidation kind for profiles.
    /// </summary>
    public const string ProfileKind = "profile";

    /// <summary>
    /// Creates a document reference invalidation.
    /// </summary>
    /// <param name="referenceType">The document reference type.</param>
    /// <param name="referenceId">The document reference identifier.</param>
    public static TabularWorkspaceInvalidation ForReference(string referenceType, string referenceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(referenceType);
        ArgumentException.ThrowIfNullOrEmpty(referenceId);

        return new TabularWorkspaceInvalidation(ReferenceKind, referenceType, referenceId);
    }

    /// <summary>
    /// Creates a chat interaction invalidation.
    /// </summary>
    /// <param name="chatInteractionId">The chat interaction identifier.</param>
    public static TabularWorkspaceInvalidation ForChatInteraction(string chatInteractionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatInteractionId);

        return new TabularWorkspaceInvalidation(ChatInteractionKind, null, chatInteractionId);
    }

    /// <summary>
    /// Creates a chat session invalidation.
    /// </summary>
    /// <param name="chatSessionId">The chat session identifier.</param>
    public static TabularWorkspaceInvalidation ForChatSession(string chatSessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(chatSessionId);

        return new TabularWorkspaceInvalidation(ChatSessionKind, null, chatSessionId);
    }

    /// <summary>
    /// Creates a profile invalidation.
    /// </summary>
    /// <param name="profileId">The profile identifier.</param>
    public static TabularWorkspaceInvalidation ForProfile(string profileId)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileId);

        return new TabularWorkspaceInvalidation(ProfileKind, null, profileId);
    }
}
