#pragma warning disable MEAI001 // Speech and realtime APIs from Microsoft.Extensions.AI are preview.
#nullable enable
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Cartesia.Services;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Speech;

/// <summary>
/// A Cartesia deployment hears the caller in a cascaded realtime deployment, through Cartesia's turns endpoint.
/// </summary>
public sealed class CartesiaRealtimeTests
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _stall = TimeSpan.FromMilliseconds(2200);

    [Fact]
    public async Task ACartesiaDeployment_GivesARealtimeClient()
    {
        // Arrange
        using var handler = new HttpClientHandler();
        var provider = new CartesiaClientProvider(new FakeHttpClientFactory(handler), NullLoggerFactory.Instance);
        var connection = new AIProviderConnectionEntry(new Dictionary<string, object> { ["ApiKey"] = "secret-key" });

        // Act
        using var client = await provider.GetRealtimeClientAsync(connection, "ink-2");

        // Assert
        Assert.IsType<CartesiaRealtimeClient>(client);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AConversationSession_IsRefused_BecauseCartesiaOnlyTranscribes(bool withOptions)
    {
        // Arrange
        using var client = new CartesiaRealtimeClient("secret-key", null, "ink-2");
        var options = withOptions ? new RealtimeSessionOptions { SessionKind = RealtimeSessionKind.Conversation } : null;

        // Act and assert
        await Assert.ThrowsAsync<NotSupportedException>(() => client.CreateSessionAsync(options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void BuildAddress_NamesTheTurnsEndpointWithTheSessionsAudioAndVersion()
    {
        var address = CartesiaRealtimeClient.BuildAddress(null, "ink-2", "pcm_s16le", 24000, 1200);

        Assert.Equal(
            "wss://api.cartesia.ai/stt/turns/websocket?model=ink-2&encoding=pcm_s16le&sample_rate=24000&cartesia_version=2026-08-14&turn_end_timeout_ms=1200",
            address.ToString());
    }

    [Fact]
    public void BuildAddress_KeepsACustomEndpointsPath()
    {
        var address = CartesiaRealtimeClient.BuildAddress(new Uri("http://localhost:5050/cartesia/"), "ink-2", "pcm_mulaw", 8000, 800);

        Assert.Equal("ws", address.Scheme);
        Assert.Equal("/cartesia/stt/turns/websocket", address.AbsolutePath);
        Assert.Contains("encoding=pcm_mulaw", address.Query, StringComparison.Ordinal);
        Assert.Contains("sample_rate=8000", address.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ink-2", null, "ink-2")]
    [InlineData("ink-2", "ink-preview", "ink-preview")]
    [InlineData("ink-whisper", null, "ink-2")]
    [InlineData(null, null, "ink-2")]
    public void GetModel_TakesTheSessionsThenTheDeploymentsModel_AndOneTheTurnsEndpointOffers(string? deploymentModel, string? sessionModel, string expected)
    {
        using var client = new CartesiaRealtimeClient("secret-key", null, deploymentModel);
        var options = new RealtimeSessionOptions
        {
            SessionKind = RealtimeSessionKind.Transcription,
            TranscriptionOptions = sessionModel is null ? null : new TranscriptionOptions { ModelId = sessionModel },
        };

        Assert.Equal(expected, client.GetModel(options));
    }

    [Theory]
    [InlineData(null, null, CartesiaRealtimeClient.DefaultTurnEndTimeoutMilliseconds)]
    [InlineData(RealtimeTurnDetectionTypes.ServerVad, 800, 800)]
    [InlineData(RealtimeTurnDetectionTypes.ServerVad, 300, 640)]
    [InlineData(RealtimeTurnDetectionTypes.SemanticVad, 800, CartesiaRealtimeClient.DefaultTurnEndTimeoutMilliseconds)]
    public void GetTurnEndTimeout_IsTheSilenceTheSessionNames_WithinCartesiasRange(string? type, int? silence, int expected)
    {
        var options = new RealtimeSessionOptions
        {
            RawRepresentationFactory = type is null
                ? null
                : () => new RealtimeTurnDetectionOverrides { Type = type, SilenceDurationMs = silence },
        };

        Assert.Equal(expected, CartesiaRealtimeClient.GetTurnEndTimeout(options));
    }

    [Fact]
    public void Assembler_ReportsANewTurnsWordsAsTheyCome_AndTheTurnOnceItEnds()
    {
        // Arrange
        long sent = 0;
        var assembler = new CartesiaTurnAssembler(() => sent, bytesPerSecond: 48000, _stall);

        // Act
        var first = assembler.Process(Event("turn.update", "Book a"));
        var second = assembler.Process(Event("turn.update", "Book a haircut"));
        var eager = assembler.Process(Event("turn.eager_end", "Book a haircut"));
        sent = 96000;
        var end = assembler.Process(Event("turn.end", "Book a haircut."));

        // Assert
        // The caller starts to speak with the turn's first words, and each partial carries only what is new.
        Assert.Equal("input_audio_buffer.speech_started", SignalType(first[0]));
        Assert.Equal("Book a", Transcription(first[1], RealtimeServerMessageType.InputAudioTranscriptionDelta));
        Assert.Equal("haircut", Transcription(Assert.Single(second), RealtimeServerMessageType.InputAudioTranscriptionDelta));
        Assert.Empty(eager);

        // The turn is committed, then reported whole, with the audio it took.
        Assert.Equal("input_audio_buffer.committed", SignalType(end[0]));
        var completed = Assert.IsType<InputAudioTranscriptionRealtimeServerMessage>(end[1]);
        Assert.Equal(RealtimeServerMessageType.InputAudioTranscriptionCompleted, completed.Type);
        Assert.Equal("Book a haircut.", completed.Transcription);
        Assert.Equal(2000L, completed.Usage!.AdditionalCounts![AIUsageAdditionalCounts.AudioDurationMs]);

        // One turn's messages share an item.
        Assert.Equal("cartesia_turn_1", completed.ItemId);
    }

    [Fact]
    public void Assembler_StartsTheNextTurnAfresh_AndMetersOnlyItsOwnAudio()
    {
        long sent = 48000;
        var assembler = new CartesiaTurnAssembler(() => sent, bytesPerSecond: 48000, _stall);
        assembler.Process(Event("turn.end", "Yes."));

        sent = 72000;
        var next = assembler.Process(Event("turn.end", "Tuesday."));

        // A turn that ends without updates is still started, committed and reported.
        Assert.Equal("input_audio_buffer.speech_started", SignalType(next[0]));
        Assert.Equal("input_audio_buffer.committed", SignalType(next[1]));
        var completed = Assert.IsType<InputAudioTranscriptionRealtimeServerMessage>(next[2]);
        Assert.Equal("cartesia_turn_2", completed.ItemId);
        Assert.Equal(500L, completed.Usage!.AdditionalCounts![AIUsageAdditionalCounts.AudioDurationMs]);
    }

    [Fact]
    public void Assembler_ATurnWithNoWords_SaysNothing()
    {
        var assembler = new CartesiaTurnAssembler(() => 0, bytesPerSecond: 48000, _stall);

        Assert.Empty(assembler.Process(Event("turn.start", null)));
        Assert.Empty(assembler.Process(Event("turn.update", "  ")));
        Assert.Empty(assembler.Process(Event("turn.update", ".")));
        Assert.Empty(assembler.Process(Event("turn.end", string.Empty)));
        Assert.Empty(assembler.Process(Event("turn.resume", null)));
        Assert.Empty(assembler.Process(Encoding.UTF8.GetBytes("not json")));
    }

    // Live, Cartesia ended "Can I book a haircut" and at once opened a turn holding only "."; read as words, it cut off the
    // reply to the haircut before a word of it was heard.
    [Fact]
    public void Assembler_PunctuationIsNotWords_SoItNeitherStartsATurnNorInterrupts()
    {
        var assembler = new CartesiaTurnAssembler(() => 0, bytesPerSecond: 48000, _stall);
        assembler.Process(Event("turn.start", null));
        assembler.Process(Event("turn.update", "Stop. please"));
        assembler.Process(Event("turn.end", "Stop. please"));

        Assert.Empty(assembler.Process(Event("turn.start", null)));
        Assert.Empty(assembler.Process(Event("turn.update", ".")));
        Assert.Empty(assembler.Process(Event("turn.eager_end", ".")));
        Assert.Empty(assembler.Process(Event("turn.end", ".")));
    }

    // Live, the caller said "Okay, stop, please" over the assistant; Cartesia ended "Okay", opened a turn for ", stop,
    // please." and left it open through the silence, so the caller heard nothing until they spoke again.
    [Fact]
    public void Assembler_EndsATurnCartesiaLeavesOpen_OnceItsWordsHaveStalled()
    {
        // Arrange
        long sent = 0;
        var time = new ManualTimeProvider();
        var assembler = new CartesiaTurnAssembler(() => sent, bytesPerSecond: 48000, _stall, time);
        assembler.Process(Event("turn.start", null));
        assembler.Process(Event("turn.update", ", stop,"));
        assembler.Process(Event("turn.update", ", stop, please."));
        sent = 96000;

        // Act
        time.Advance(_stall - TimeSpan.FromMilliseconds(1));
        var early = assembler.EndStalledTurn();
        time.Advance(TimeSpan.FromMilliseconds(1));
        var stalled = assembler.EndStalledTurn();
        var again = assembler.EndStalledTurn();
        var late = assembler.Process(Event("turn.end", ", stop, please."));

        // Assert
        Assert.Empty(early);
        Assert.Equal("input_audio_buffer.committed", SignalType(stalled[0]));
        var completed = Assert.IsType<InputAudioTranscriptionRealtimeServerMessage>(stalled[1]);
        Assert.Equal("stop, please.", completed.Transcription);
        Assert.Equal(2000L, completed.Usage!.AdditionalCounts![AIUsageAdditionalCounts.AudioDurationMs]);

        // The turn is ended once, and Cartesia's own end of it, carrying nothing new, says nothing.
        Assert.Empty(again);
        Assert.Empty(late);
    }

    [Fact]
    public void Assembler_WordsCartesiaHearsAfterATurnEndedHere_AreATurnOfTheirOwn()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var assembler = new CartesiaTurnAssembler(() => 0, bytesPerSecond: 48000, _stall, time);
        assembler.Process(Event("turn.start", null));
        assembler.Process(Event("turn.update", "Stop"));
        time.Advance(_stall);
        var stalled = assembler.EndStalledTurn();

        // Act
        var more = assembler.Process(Event("turn.update", "Stop please"));
        var end = assembler.Process(Event("turn.end", "Stop please."));

        // Assert
        Assert.Equal("Stop", Assert.IsType<InputAudioTranscriptionRealtimeServerMessage>(stalled[^1]).Transcription);
        Assert.Equal("input_audio_buffer.speech_started", SignalType(more[0]));
        Assert.Equal("please", Transcription(more[1], RealtimeServerMessageType.InputAudioTranscriptionDelta));
        var completed = Assert.IsType<InputAudioTranscriptionRealtimeServerMessage>(end[^1]);
        Assert.Equal("please.", completed.Transcription);
        Assert.NotEqual(Assert.IsType<InputAudioTranscriptionRealtimeServerMessage>(stalled[^1]).ItemId, completed.ItemId);
    }

    [Fact]
    public void Assembler_LeavesATurnCartesiaIsStillHearing()
    {
        var time = new ManualTimeProvider();
        var assembler = new CartesiaTurnAssembler(() => 0, bytesPerSecond: 48000, _stall, time);
        assembler.Process(Event("turn.start", null));
        assembler.Process(Event("turn.update", "I would like"));

        // Each event of the turn restarts the wait, and a turn with no words is never ended here.
        time.Advance(_stall - TimeSpan.FromMilliseconds(100));
        assembler.Process(Event("turn.update", "I would like to book"));
        time.Advance(_stall - TimeSpan.FromMilliseconds(100));
        Assert.Empty(assembler.EndStalledTurn());

        assembler.Process(Event("turn.end", "I would like to book."));
        assembler.Process(Event("turn.start", null));
        assembler.Process(Event("turn.update", "."));
        time.Advance(_stall * 3);
        Assert.Empty(assembler.EndStalledTurn());
    }

    [Fact]
    public void Assembler_ReportsCartesiasError()
    {
        var assembler = new CartesiaTurnAssembler(() => 0, bytesPerSecond: 48000, _stall);

        var error = Assert.IsType<ErrorRealtimeServerMessage>(Assert.Single(assembler.Process(
            Encoding.UTF8.GetBytes("""{"type":"error","status_code":400,"title":"Invalid model","message":"ink-whisper is not supported"}"""))));

        Assert.Equal("Cartesia reported an error: Invalid model: ink-whisper is not supported", error.Error?.Message);
    }

    [Fact]
    public async Task Session_SendsTheCallersAudio_AndReportsCartesiasTurns()
    {
        // Arrange
        await using var pair = await SocketPair.ConnectAsync();
        var options = new RealtimeSessionOptions { SessionKind = RealtimeSessionKind.Transcription };
        var session = new CartesiaTranscriptionSession(pair.Client, options, bytesPerSecond: 48000, turnEndTimeoutMilliseconds: 1200);
        var audio = new byte[] { 1, 2, 3, 4 };

        // Act
        await session.SendAsync(new InputAudioBufferAppendRealtimeClientMessage(new DataContent(audio, "audio/pcm")), TestContext.Current.CancellationToken);
        var received = await pair.ReceiveAsync();

        await pair.SendTextAsync("""{"type":"turn.update","transcript":"Hello"}""");
        await pair.SendTextAsync("""{"type":"turn.end","transcript":"Hello."}""");

        var messages = new List<RealtimeServerMessage>();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(_wait);

        await foreach (var message in session.GetStreamingResponseAsync(timeout.Token))
        {
            messages.Add(message);

            if (message.Type == RealtimeServerMessageType.InputAudioTranscriptionCompleted)
            {
                break;
            }
        }

        // The session is told the stream is over when it ends.
        var disposing = session.DisposeAsync();
        var closing = await pair.ReceiveAsync();
        await disposing;

        // Assert
        Assert.Equal(WebSocketMessageType.Binary, received.Type);
        Assert.Equal(audio, received.Data);
        Assert.Contains(messages, message => message.Type == RealtimeServerMessageType.InputAudioTranscriptionDelta);
        Assert.Equal("Hello.", Assert.IsType<InputAudioTranscriptionRealtimeServerMessage>(messages[^1]).Transcription);
        Assert.Equal("""{"type":"close"}""", closing.Text);
    }

    // A wrong key surfaced as "The server returned status code '401' when status code '101' was expected", which does not
    // say what to fix.
    [Theory]
    [InlineData("401 Unauthorized", "API key")]
    [InlineData("400 Bad Request", "model or audio format")]
    public async Task ARefusedConnection_SaysWhatCartesiaRefused(string status, string expected)
    {
        // Arrange
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var refusing = RefuseAsync(listener, status);

        var endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
        using var client = new CartesiaRealtimeClient("wrong-key", endpoint, "ink-2");
        var options = new RealtimeSessionOptions { SessionKind = RealtimeSessionKind.Transcription };

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateSessionAsync(options, TestContext.Current.CancellationToken));
        await refusing;

        // Assert
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    private static byte[] Event(string type, string? transcript)
        => transcript is null
            ? Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type }))
            : Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, transcript }));

    private static string? SignalType(RealtimeServerMessage message)
        => message.RawRepresentation is JsonElement raw && raw.TryGetProperty("type", out var type) ? type.GetString() : null;

    private static string? Transcription(RealtimeServerMessage message, RealtimeServerMessageType type)
    {
        var transcription = Assert.IsType<InputAudioTranscriptionRealtimeServerMessage>(message);
        Assert.Equal(type, transcription.Type);

        return transcription.Transcription;
    }

    // Answers the WebSocket upgrade with a refusal, as Cartesia does when it will not open the session.
    private static async Task RefuseAsync(TcpListener listener, string status)
    {
        using var connection = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        using var stream = connection.GetStream();

        var buffer = new byte[4096];
        var request = new StringBuilder();

        while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);

            if (read == 0)
            {
                break;
            }

            request.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A clock that moves only when told to.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
            => _timestamp;

        public void Advance(TimeSpan by)
            => _timestamp += by.Ticks;
    }

    /// <summary>
    /// Two WebSocket ends joined over a loopback TCP connection: the client end for the session, the server end standing
    /// in for Cartesia.
    /// </summary>
    private sealed class SocketPair : IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly TcpClient _server;

        private SocketPair(TcpClient client, TcpClient server)
        {
            _client = client;
            _server = server;
            Client = WebSocket.CreateFromStream(client.GetStream(), new WebSocketCreationOptions { IsServer = false });
            Server = WebSocket.CreateFromStream(server.GetStream(), new WebSocketCreationOptions { IsServer = true });
        }

        public WebSocket Client { get; }

        public WebSocket Server { get; }

        public static async Task<SocketPair> ConnectAsync()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            var client = new TcpClient();
            var accepting = listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, TestContext.Current.CancellationToken);

            return new SocketPair(client, await accepting);
        }

        public async Task<(WebSocketMessageType Type, byte[] Data, string? Text)> ReceiveAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(_wait);

            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            WebSocketReceiveResult result;

            do
            {
                result = await Server.ReceiveAsync(chunk, timeout.Token);
                buffer.Write(chunk, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var data = buffer.ToArray();

            return (result.MessageType, data, result.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(data) : null);
        }

        public Task SendTextAsync(string text)
            => Server.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, TestContext.Current.CancellationToken);

        public ValueTask DisposeAsync()
        {
            Server.Dispose();
            Client.Dispose();
            _server.Dispose();
            _client.Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
