namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Keys the realtime orchestrator publishes on <c>OrchestrationContext.Properties</c> so shared orchestration
/// handlers can adapt to how the session will actually be run.
/// </summary>
public static class RealtimeOrchestrationContextKeys
{
    /// <summary>
    /// Set to <see langword="true"/> when the session retrieves knowledge for every turn before the model
    /// answers (see <see cref="IRealtimeTurnGrounding"/>). Handlers that would otherwise instruct the model to
    /// go and search for itself should stand down: the knowledge is already in front of it, and a search on top
    /// of that costs a round-trip in the one place latency is audible.
    /// </summary>
    public const string GroundingEnabled = "RealtimeGroundingEnabled";
}
