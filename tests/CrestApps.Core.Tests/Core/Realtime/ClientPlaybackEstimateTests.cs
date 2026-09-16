#nullable enable
using CrestApps.Core.AI.Chat.Realtime;

namespace CrestApps.Core.Tests.Core.Realtime;

public class ClientPlaybackEstimateTests
{
    // PCM16 mono at 24 kHz: 48 bytes to the millisecond.
    private const int BytesPerMillisecond = 48;

    [Fact]
    public void PendingMs_WithNothingHandedOver_IsZero()
    {
        var estimate = new ClientPlaybackEstimate(new FixedTimeProvider(DateTimeOffset.UtcNow));

        Assert.Equal(0, estimate.PendingMs);
    }

    [Fact]
    public void PendingMs_CountsAudioHandedToTheClientButNotYetHeard()
    {
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var estimate = new ClientPlaybackEstimate(time);

        estimate.Append(1000 * BytesPerMillisecond);

        Assert.Equal(1000, estimate.PendingMs);
    }

    [Fact]
    public void PendingMs_QueuesAChunkBehindWhatIsStillPlaying()
    {
        // The whole point: a deployment that synthesizes faster than real time hands over far more audio than the
        // time it took to send it, and the client plays it at real time regardless.
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var estimate = new ClientPlaybackEstimate(time);

        for (var i = 0; i < 30; i++)
        {
            estimate.Append(1000 * BytesPerMillisecond);
            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        // Thirty seconds of speech sent in three seconds: twenty-seven of it is still waiting to be heard.
        Assert.Equal(27_000, estimate.PendingMs);
    }

    [Fact]
    public void PendingMs_DrainsWithTheClock()
    {
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var estimate = new ClientPlaybackEstimate(time);

        estimate.Append(1000 * BytesPerMillisecond);
        time.Advance(TimeSpan.FromMilliseconds(400));

        Assert.Equal(600, estimate.PendingMs);

        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(0, estimate.PendingMs);
    }

    [Fact]
    public void PendingMs_AfterTheClientDropsItsQueue_IsZero()
    {
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var estimate = new ClientPlaybackEstimate(time);

        estimate.Append(30_000 * BytesPerMillisecond);

        estimate.Flush();

        Assert.Equal(0, estimate.PendingMs);
    }

    [Fact]
    public void PendingMs_AfterAGapInPlayback_CountsOnlyTheNewAudio()
    {
        var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var estimate = new ClientPlaybackEstimate(time);

        estimate.Append(1000 * BytesPerMillisecond);
        time.Advance(TimeSpan.FromMinutes(1));

        estimate.Append(500 * BytesPerMillisecond);

        Assert.Equal(500, estimate.PendingMs);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    }
}
