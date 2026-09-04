#nullable enable
using CrestApps.Core.AI.Realtime;

namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// A handle on a running realtime session, used to change turn-taking settings without ending the conversation.
/// </summary>
/// <remarks>
/// Barge-in and the voice-activity knobs are enforced in three places at once — the browser's microphone gate,
/// the server's input pump, and the provider's own turn detection. Changing only one of them leaves them
/// disagreeing (the classic symptom: the user turns barge-in off mid-conversation, the client stops sending, but
/// the provider still interrupts itself). This applies the change to the two server-side halves together.
/// </remarks>
public sealed class RealtimeSessionControl
{
    private readonly IRealtimeConversation _conversation;
    private readonly RealtimeChatRunContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="RealtimeSessionControl"/> class.
    /// </summary>
    /// <param name="conversation">The live provider conversation.</param>
    /// <param name="context">The run context whose settings the input pump reads.</param>
    internal RealtimeSessionControl(IRealtimeConversation conversation, RealtimeChatRunContext context)
    {
        _conversation = conversation;
        _context = context;
    }

    /// <summary>
    /// Gets the identifier of the session this control belongs to.
    /// </summary>
    public string SessionId => _context.SessionId;

    /// <summary>
    /// Applies new turn-taking settings to the running session.
    /// </summary>
    /// <param name="allowInterruption">Whether the user may talk over the assistant.</param>
    /// <param name="silenceDurationMs">The silence, in milliseconds, that ends a user turn, or null to keep the current value.</param>
    /// <param name="vadThreshold">The voice-activity detection threshold (0.0–1.0), or null to keep the current value.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task ApplyTurnDetectionAsync(
        bool allowInterruption,
        int? silenceDurationMs,
        float? vadThreshold,
        CancellationToken cancellationToken = default)
    {
        // Update the pump's view first: it is the hard guarantee that audio spoken over the assistant is not
        // forwarded, and it takes effect on the very next microphone frame.
        _context.AllowInterruption = allowInterruption;

        if (silenceDurationMs.HasValue)
        {
            _context.SilenceDurationMs = silenceDurationMs;
        }

        if (vadThreshold.HasValue)
        {
            _context.VadThreshold = vadThreshold;
        }

        await _conversation.UpdateTurnDetectionAsync(
            allowInterruption, _context.SilenceDurationMs, _context.VadThreshold, turnDetectionType: null, cancellationToken);
    }
}
