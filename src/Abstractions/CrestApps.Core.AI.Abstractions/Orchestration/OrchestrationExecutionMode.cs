namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Identifies how an <see cref="OrchestrationContext"/> will be executed once it has been prepared.
/// The <c>PREPARE</c> stage (system message, tools, RAG) is shared across execution modes; this flag
/// lets shared handlers make the few decisions that legitimately differ between a turn-based text
/// completion and a persistent speech-to-speech realtime session.
/// </summary>
public enum OrchestrationExecutionMode
{
    /// <summary>
    /// The context is executed as a turn-based text completion via <see cref="IOrchestrator"/> and
    /// <c>IChatClient</c>. This is the default for every existing chat and interaction path.
    /// </summary>
    Chat,

    /// <summary>
    /// The context is executed as a persistent, bidirectional speech-to-speech session via
    /// <c>IRealtimeOrchestrator</c> and <c>IRealtimeClient</c>. There is no up-front user message,
    /// so preemptive RAG is skipped and knowledge retrieval is surfaced as a callable search tool.
    /// </summary>
    Realtime,
}
