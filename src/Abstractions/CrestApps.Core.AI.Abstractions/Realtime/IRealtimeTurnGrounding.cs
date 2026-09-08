using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Runs preemptive knowledge retrieval for a single spoken utterance so a realtime session can be grounded
/// the same way a text completion is.
/// </summary>
/// <remarks>
/// <para>
/// The shared <c>PreemptiveRagOrchestrationHandler</c> keys off the user message, which does not exist when a
/// realtime session is prepared — the session opens before anyone has said anything. Retrieval therefore cannot
/// happen at PREPARE time; it has to happen once per turn, as soon as the provider has transcribed what the user
/// said and before the model is asked to answer.
/// </para>
/// <para>
/// This is what closes the gap with the text path. Leaving retrieval to the model's own judgement (the search
/// tool) means an answer is only grounded when the model chooses to call the tool, which realtime models do far
/// less reliably than their text counterparts.
/// </para>
/// </remarks>
public interface IRealtimeTurnGrounding
{
    /// <summary>
    /// Determines whether the prepared session should have its turns grounded. Returns <see langword="false"/>
    /// when preemptive retrieval is switched off site-wide, when tools/knowledge are unavailable, or when the
    /// resource has nothing to retrieve from — in which case the session keeps the provider's own automatic
    /// turn handling and relies on the search tool alone.
    /// </summary>
    /// <param name="context">The prepared orchestration context for the session.</param>
    /// <param name="resource">The resource driving the session (an <c>AIProfile</c> or <c>ChatInteraction</c>).</param>
    bool IsGroundingAvailable(OrchestrationContext context, object resource);

    /// <summary>
    /// Retrieves knowledge relevant to one utterance and returns the context block to hand the model, or
    /// <see langword="null"/> when nothing relevant was found. Citations discovered during retrieval are recorded
    /// on the ambient <c>AIInvocationScope</c> so the turn carries them exactly as a tool-driven turn would.
    /// </summary>
    /// <param name="context">The prepared orchestration context for the session.</param>
    /// <param name="resource">The resource driving the session.</param>
    /// <param name="utterance">The transcript of what the user just said.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<string> RetrieveAsync(OrchestrationContext context, object resource, string utterance, CancellationToken cancellationToken = default);
}
