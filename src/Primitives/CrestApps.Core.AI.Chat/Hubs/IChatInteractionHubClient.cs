using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Chat.Hubs;

/// <summary>
/// Defines the SignalR client methods that the chat interaction hub can invoke
/// on connected clients, covering error reporting, interaction loading,
/// settings persistence, and history management.
/// </summary>
public interface IChatInteractionHubClient
{
    /// <summary>
    /// Sends an error message to the client.
    /// </summary>
    /// <param name="error">The error message text.</param>
    Task ReceiveError(string error);

    /// <summary>
    /// Loads interaction data on the client, replacing the current state.
    /// </summary>
    /// <param name="data">The serialized interaction data.</param>
    Task LoadInteraction(object data);

    /// <summary>
    /// Notifies the client that interaction settings have been saved.
    /// </summary>
    /// <param name="itemId">The identifier of the content item associated with the interaction.</param>
    /// <param name="title">The updated title of the interaction.</param>
    Task SettingsSaved(string itemId, string title);

    /// <summary>
    /// Notifies the client that the chat history has been cleared.
    /// </summary>
    /// <param name="itemId">The identifier of the content item whose history was cleared.</param>
    Task HistoryCleared(string itemId);

    /// <summary>
    /// Receives the transcript.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    /// <param name="text">The text.</param>
    /// <param name="isFinal">Indicates whether final.</param>
    Task ReceiveTranscript(string identifier, string text, bool isFinal);

    /// <summary>
    /// Receives the audio Chunk.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    /// <param name="base64Audio">The base 64 audio.</param>
    /// <param name="contentType">The content type.</param>
    Task ReceiveAudioChunk(string identifier, string base64Audio, string contentType);

    /// <summary>
    /// Receives the audio Complete.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    Task ReceiveAudioComplete(string identifier);

    /// <summary>
    /// Receives the conversation User Message.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    /// <param name="text">The text.</param>
    Task ReceiveConversationUserMessage(string identifier, string text);

    /// <summary>
    /// Receives the conversation Assistant Token.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    /// <param name="messageId">The message id.</param>
    /// <param name="token">The token.</param>
    /// <param name="responseId">The response id.</param>
    /// <param name="references">The references.</param>
    /// <param name="appearance">The appearance.</param>
    Task ReceiveConversationAssistantToken(
        string identifier,
        string messageId,
        string token,
        string responseId,
        Dictionary<string, AICompletionReference> references = null,
        AssistantMessageAppearance appearance = null);

    /// <summary>
    /// Receives the conversation Assistant Complete.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    /// <param name="messageId">The message id.</param>
    /// <param name="references">The references.</param>
    Task ReceiveConversationAssistantComplete(
        string identifier,
        string messageId,
        Dictionary<string, AICompletionReference> references = null);

    /// <summary>
    /// Receives the notification.
    /// </summary>
    /// <param name="notification">The notification.</param>
    Task ReceiveNotification(ChatNotification notification);

    /// <summary>
    /// Updates the notification.
    /// </summary>
    /// <param name="notification">The notification.</param>
    Task UpdateNotification(ChatNotification notification);

    /// <summary>
    /// Removes the notification.
    /// </summary>
    /// <param name="notificationType">The notification type.</param>
    Task RemoveNotification(string notificationType);
}
