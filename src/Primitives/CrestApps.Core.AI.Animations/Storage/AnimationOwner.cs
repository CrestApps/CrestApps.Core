namespace CrestApps.Core.AI.Animations.Storage;

/// <summary>
/// The conversation an animation belongs to: a chat interaction or a chat session.
/// </summary>
/// <param name="ReferenceType">The type of conversation, such as <c>chat-interaction</c> or <c>chat-session</c>.</param>
/// <param name="ReferenceId">The conversation's identifier.</param>
public sealed record AnimationOwner(string ReferenceType, string ReferenceId);
