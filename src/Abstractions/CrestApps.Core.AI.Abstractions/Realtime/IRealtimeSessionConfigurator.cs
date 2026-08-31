#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// Maps a prepared realtime request into the <see cref="RealtimeSessionOptions"/> handed to a provider
/// realtime session. This is a pure transform (no I/O): the orchestrator resolves the variable inputs
/// (instructions built by the orchestration pipeline, resolved voice, materialized tools, deployment
/// model) and this type assembles them into provider-neutral session options with sensible audio and
/// turn-detection defaults. Hosts can replace it to change defaults without touching the orchestrator.
/// </summary>
public interface IRealtimeSessionConfigurator
{
    /// <summary>
    /// Builds the <see cref="RealtimeSessionOptions"/> for the given request.
    /// </summary>
    /// <param name="context">The resolved inputs for the session.</param>
    RealtimeSessionOptions Configure(RealtimeSessionConfiguratorContext context);
}
