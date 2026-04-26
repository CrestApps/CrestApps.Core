using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.ResponseHandling;

/// <summary>
/// Defines a pluggable handler responsible for processing chat prompts and producing responses.
/// Implementations may generate responses in real time (streaming) or defer them
/// for asynchronous delivery (e.g., via webhook from a third-party agent platform).
/// </summary>
/// <remarks>
/// <para>The default implementation routes prompts through the AI orchestration pipeline.
/// Custom implementations can route prompts to external systems such as live-agent
/// platforms (e.g., Genesys, Twilio Flex) or any other backend capable of handling
/// human-to-human or hybrid conversations.</para>
///
/// <para>The active handler for a session is determined by
/// <see cref="AIChatSession.ResponseHandlerName"/> or
/// <see cref="ChatInteraction.ResponseHandlerName"/>. An AI function or external
/// event can change this value mid-conversation to transfer the chat to a different handler.</para>
/// </remarks>
public interface IChatResponseHandler
{
    /// <summary>
    /// Gets the unique technical name of this handler (e.g., <c>"AI"</c>, <c>"Genesys"</c>).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Handles the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<ChatResponseHandlerResult> HandleAsync(
        ChatResponseHandlerContext context,
        CancellationToken cancellationToken = default);
}
