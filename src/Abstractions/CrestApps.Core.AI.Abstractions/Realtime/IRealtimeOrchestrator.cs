using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Prepares and runs a speech-to-speech session for an AI resource (an <see cref="AIProfile"/> or
/// <see cref="ChatInteraction"/>). It is the realtime sibling of <see cref="IOrchestrator"/>: it reuses
/// the shared PREPARE pipeline (system message, tools, RAG guidance) but executes over a persistent
/// bidirectional <c>IRealtimeClient</c> session instead of a turn-based <c>IChatClient</c>.
/// </summary>
/// <remarks>
/// The caller must establish an <see cref="AIInvocationScope"/> before calling
/// <see cref="StartAsync"/> and keep it alive for the lifetime of the returned conversation, so that AI
/// tools invoked during the session observe the correct ambient context (data source, resource, session).
/// </remarks>
public interface IRealtimeOrchestrator
{
    /// <summary>
    /// Prepares the orchestration context, configures a provider realtime session with the resource's
    /// instructions and tools, and returns a live conversation to drive.
    /// </summary>
    /// <param name="request">The request describing the resource and session parameters.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<IRealtimeConversation> StartAsync(RealtimeOrchestrationRequest request, CancellationToken cancellationToken = default);
}
