#nullable enable
namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Remembers the prompt records created for recent user utterances so their text can be filled in when
/// transcription completes, or they can be removed when it fails or the utterance was never answered.
/// </summary>
/// <remarks>
/// A realtime turn is created before it can be transcribed, so the store has to hold the record it just wrote
/// until the text catches up. Only a handful of utterances are ever in flight at once, so the map is capped and
/// evicts oldest-first rather than growing for the length of the conversation.
/// </remarks>
/// <typeparam name="TPrompt">The host's prompt record type.</typeparam>
internal sealed class PendingUserTurnTracker<TPrompt>
    where TPrompt : class
{
    private const int MaxTracked = 16;

    private readonly Dictionary<string, TPrompt> _pending = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    /// <summary>
    /// Starts tracking the record written for a turn.
    /// </summary>
    /// <param name="turnId">The turn id.</param>
    /// <param name="prompt">The persisted record.</param>
    public void Track(string turnId, TPrompt prompt)
    {
        if (_pending.ContainsKey(turnId))
        {
            _pending[turnId] = prompt;

            return;
        }

        while (_order.Count >= MaxTracked && _order.TryDequeue(out var oldest))
        {
            _pending.Remove(oldest);
        }

        _pending[turnId] = prompt;
        _order.Enqueue(turnId);
    }

    /// <summary>
    /// Stops tracking a turn and returns the record written for it, or <see langword="null"/> when it is no longer
    /// tracked.
    /// </summary>
    /// <param name="turnId">The turn id.</param>
    public TPrompt? Take(string turnId)
    {
        if (!_pending.Remove(turnId, out var prompt))
        {
            return null;
        }

        return prompt;
    }
}
