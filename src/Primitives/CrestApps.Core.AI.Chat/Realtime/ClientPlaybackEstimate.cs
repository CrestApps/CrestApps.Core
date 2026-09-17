#nullable enable
namespace CrestApps.Core.AI.Chat.Realtime;

/// <summary>
/// Tracks how much assistant audio a pass-through transport has handed to the client but the client has not
/// finished playing.
/// </summary>
/// <remarks>
/// A transport that forwards each chunk as it arrives does not stop having a playback queue — it moves the queue
/// into the browser, which plays the audio at real time out of its own audio graph. A deployment that synthesizes
/// faster than real time can therefore hand over a minute of speech in a few seconds, and the server would
/// otherwise believe the reply was over the moment it finished sending it. Since the audio is played at real time,
/// how much of it is left can be derived from the clock: each chunk is queued behind whatever is still playing,
/// and the queue drains one millisecond per millisecond.
/// </remarks>
internal sealed class ClientPlaybackEstimate
{
    // The realtime pipeline's audio format: PCM16 mono at 24 kHz, so two bytes per sample.
    private const int BytesPerMillisecond = 24000 / 1000 * 2;

    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();

    // When the audio handed over so far will have finished playing.
    private DateTimeOffset _drainsAtUtc;

    public ClientPlaybackEstimate(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Gets the estimated amount of audio, in milliseconds, still waiting to be heard.
    /// </summary>
    public int PendingMs
    {
        get
        {
            lock (_sync)
            {
                var remaining = _drainsAtUtc - _timeProvider.GetUtcNow();

                return remaining <= TimeSpan.Zero
                    ? 0
                    : (int)remaining.TotalMilliseconds;
            }
        }
    }

    /// <summary>
    /// Records a chunk of assistant audio handed to the client.
    /// </summary>
    /// <param name="byteCount">The size of the chunk, in bytes.</param>
    public void Append(int byteCount)
    {
        if (byteCount <= 0)
        {
            return;
        }

        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();

            // A chunk arriving while the client is still playing is queued behind it; one arriving after the queue
            // ran dry starts playing now.
            var startsAtUtc = _drainsAtUtc > now
                ? _drainsAtUtc
                : now;

            _drainsAtUtc = startsAtUtc.AddMilliseconds((double)byteCount / BytesPerMillisecond);
        }
    }

    /// <summary>
    /// Records that the client has dropped whatever it had queued, so nothing is waiting to be heard.
    /// </summary>
    public void Flush()
    {
        lock (_sync)
        {
            _drainsAtUtc = default;
        }
    }
}
