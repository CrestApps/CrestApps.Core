using CrestApps.Core.AI.Chat.Realtime;
using CrestApps.Core.AI.Realtime;

namespace CrestApps.Core.Tests.Core.Realtime;

public class WebRtcRealtimePeerRegistryTests
{
    [Fact]
    public void AddIceCandidate_DeliversCandidatesThatArrivedBeforeThePeerWasRegistered()
    {
        // The browser trickles its candidates the moment it has them, which is seconds before the peer finishes
        // being built (socket bind, ICE server DNS, TURN allocation, setRemoteDescription). Dropping that window
        // left the peer with no remote candidates, so it formed no pairs and ICE timed out on both sides.
        var registry = new WebRtcRealtimePeerRegistry();
        var peer = new FakeWebRtcRealtimePeer();

        registry.AddIceCandidate("connection-1", new WebRtcIceCandidate { Candidate = "host" });
        registry.AddIceCandidate("connection-1", new WebRtcIceCandidate { Candidate = "relay" });

        Assert.Empty(peer.Candidates);

        registry.Add("connection-1", peer);

        Assert.Equal(["host", "relay"], peer.Candidates.Select(c => c.Candidate));
    }

    [Fact]
    public void AddIceCandidate_DeliversStraightToAnAlreadyRegisteredPeer()
    {
        var registry = new WebRtcRealtimePeerRegistry();
        var peer = new FakeWebRtcRealtimePeer();
        registry.Add("connection-1", peer);

        registry.AddIceCandidate("connection-1", new WebRtcIceCandidate { Candidate = "srflx" });

        Assert.Equal(["srflx"], peer.Candidates.Select(c => c.Candidate));
    }

    [Fact]
    public void AddIceCandidate_KeepsArrivalOrderAcrossTheRegistrationBoundary()
    {
        // The empty candidate is how the browser signals the end of gathering; it must not overtake a real one.
        var registry = new WebRtcRealtimePeerRegistry();
        var peer = new FakeWebRtcRealtimePeer();

        registry.AddIceCandidate("connection-1", new WebRtcIceCandidate { Candidate = "host" });
        registry.Add("connection-1", peer);
        registry.AddIceCandidate("connection-1", new WebRtcIceCandidate { Candidate = string.Empty });

        Assert.Equal(["host", ""], peer.Candidates.Select(c => c.Candidate));
    }

    [Fact]
    public void AddIceCandidate_DoesNotGrowWithoutBoundForAConnectionThatNeverGetsAPeer()
    {
        var registry = new WebRtcRealtimePeerRegistry();
        var peer = new FakeWebRtcRealtimePeer();

        for (var i = 0; i < 500; i++)
        {
            registry.AddIceCandidate("connection-1", new WebRtcIceCandidate { Candidate = $"host-{i}" });
        }

        registry.Add("connection-1", peer);

        Assert.Equal(64, peer.Candidates.Count);
    }

    [Fact]
    public void Remove_DiscardsCandidatesHeldForThatConnection()
    {
        var registry = new WebRtcRealtimePeerRegistry();
        var peer = new FakeWebRtcRealtimePeer();

        registry.AddIceCandidate("connection-1", new WebRtcIceCandidate { Candidate = "host" });
        registry.Remove("connection-1");
        registry.Add("connection-1", peer);

        Assert.Empty(peer.Candidates);
    }

    [Fact]
    public void AddIceCandidate_RoutesEachConnectionToItsOwnPeer()
    {
        var registry = new WebRtcRealtimePeerRegistry();
        var first = new FakeWebRtcRealtimePeer();
        var second = new FakeWebRtcRealtimePeer();

        registry.AddIceCandidate("connection-1", new WebRtcIceCandidate { Candidate = "first" });
        registry.AddIceCandidate("connection-2", new WebRtcIceCandidate { Candidate = "second" });
        registry.Add("connection-1", first);
        registry.Add("connection-2", second);

        Assert.Equal(["first"], first.Candidates.Select(c => c.Candidate));
        Assert.Equal(["second"], second.Candidates.Select(c => c.Candidate));
    }

    private sealed class FakeWebRtcRealtimePeer : IWebRtcRealtimePeer
    {
        public List<WebRtcIceCandidate> Candidates { get; } = [];

        public string AnswerSdp => string.Empty;

        public int QueuedPlaybackMs => 0;

        public event Action<WebRtcIceCandidate> IceCandidateGenerated { add { } remove { } }

        public event Action Connected { add { } remove { } }

        public event Action Closed { add { } remove { } }

        public void AddIceCandidate(WebRtcIceCandidate candidate)
            => Candidates.Add(candidate);

        public IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAudioAsync(CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<ReadOnlyMemory<byte>>();

        public void SendAudio(ReadOnlyMemory<byte> pcm24k)
        {
        }

        public void FlushPlayback()
        {
        }

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }
}
