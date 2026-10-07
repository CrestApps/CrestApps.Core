using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only and requires explicit opt-in at each usage site.
/// <summary>
/// Meters every realtime session started through a realtime client the AI client factory created, whatever the
/// provider. Each completed response and each completed transcription of the caller's speech is recorded from the
/// usage the provider reported on it.
/// </summary>
internal sealed class AIUsageTrackingRealtimeClient : DelegatingRealtimeClient
{
    private readonly AIUsageRecorder _recorder;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIUsageTrackingRealtimeClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner client.</param>
    /// <param name="recorder">The recorder that stores the usage.</param>
    public AIUsageTrackingRealtimeClient(
        IRealtimeClient innerClient,
        AIUsageRecorder recorder)
        : base(innerClient)
    {
        _recorder = recorder;
    }

    /// <summary>
    /// Creates a session whose responses and transcriptions are metered.
    /// </summary>
    /// <param name="options">The session options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task<IRealtimeClientSession> CreateSessionAsync(
        RealtimeSessionOptions options = null,
        CancellationToken cancellationToken = default)
    {
        var session = await base.CreateSessionAsync(options, cancellationToken);

        if (session is null)
        {
            return null;
        }

        return new AIUsageTrackingRealtimeSession(session, _recorder, CaptureUsageProperties());
    }

    /// <summary>
    /// Captures the labels and conversation in effect when the session starts. A session's messages can be read
    /// from a different async flow than the one that started it, so they are not looked up per message.
    /// </summary>
    private static Dictionary<string, object> CaptureUsageProperties()
    {
        var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (AIUsageScope.Current is { } usage)
        {
            if (!string.IsNullOrEmpty(usage.ContextType))
            {
                properties[AICompletionContextKeys.UsageContextType] = usage.ContextType;
            }

            if (!string.IsNullOrEmpty(usage.Purpose))
            {
                properties[AICompletionContextKeys.UsagePurpose] = usage.Purpose;
            }
        }

        var invocation = AIInvocationScope.Current;

        if (invocation?.ChatSession is { } session)
        {
            properties[AICompletionContextKeys.Session] = session;
        }

        if (invocation?.ChatInteraction is { } interaction)
        {
            properties[AICompletionContextKeys.Interaction] = interaction;
            properties[AICompletionContextKeys.InteractionId] = interaction.ItemId;
        }

        return properties;
    }
}
#pragma warning restore MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only and requires explicit opt-in at each usage site.
