#pragma warning disable MEAI001
#nullable enable
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure.Realtime;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Tests for the temporary Azure OpenAI realtime WebSocket transport
/// (<c>CrestApps.Core.AI.OpenAI.Azure.Realtime</c>): the message/JSON mapping, the session send/receive loop,
/// the client connection URL/auth, and the connection factory.
/// </summary>
public sealed class AzureRealtimeTests
{
    // ---- AzureRealtimeProtocol: client message -> JSON ----

    [Fact]
    public void WriteClientMessage_SessionUpdate_EmitsSessionUpdateEvent()
    {
        var options = new RealtimeSessionOptions
        {
            Instructions = "Be helpful.",
            Voice = "alloy",
            InputAudioFormat = new RealtimeAudioFormat("audio/pcm", 24000),
            OutputAudioFormat = new RealtimeAudioFormat("audio/pcm", 24000),
            OutputModalities = ["audio", "text"],
            VoiceActivityDetection = new VoiceActivityDetectionOptions { Enabled = true, AllowInterruption = true },
        };

        var json = WriteToJson(new SessionUpdateRealtimeClientMessage(options));

        Assert.Equal("session.update", json.GetProperty("type").GetString());
        var session = json.GetProperty("session");
        Assert.Equal("realtime", session.GetProperty("type").GetString());
        Assert.Equal("Be helpful.", session.GetProperty("instructions").GetString());
        Assert.Equal(JsonValueKind.Array, session.GetProperty("output_modalities").ValueKind);

        var audio = session.GetProperty("audio");
        var input = audio.GetProperty("input");
        Assert.Equal("audio/pcm", input.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal(24000, input.GetProperty("format").GetProperty("rate").GetInt32());
        Assert.Equal("server_vad", input.GetProperty("turn_detection").GetProperty("type").GetString());

        var output = audio.GetProperty("output");
        Assert.Equal("alloy", output.GetProperty("voice").GetString());
        Assert.Equal("audio/pcm", output.GetProperty("format").GetProperty("type").GetString());
    }

    [Fact]
    public void WriteClientMessage_SessionUpdate_SemanticVad_WritesEagernessAndNoSilenceKnobs()
    {
        // The server-VAD knobs are rejected alongside semantic_vad, so the two sets are never mixed on the wire.
        var options = new RealtimeSessionOptions
        {
            VoiceActivityDetection = new VoiceActivityDetectionOptions { Enabled = true, AllowInterruption = false },
            RawRepresentationFactory = () => new CrestApps.Core.AI.Realtime.RealtimeTurnDetectionOverrides
            {
                Type = CrestApps.Core.AI.Realtime.RealtimeTurnDetectionTypes.SemanticVad,
                Eagerness = "low",
                SilenceDurationMs = 800,
            },
        };

        var json = WriteToJson(new SessionUpdateRealtimeClientMessage(options));

        var turnDetection = json.GetProperty("session").GetProperty("audio").GetProperty("input").GetProperty("turn_detection");
        Assert.Equal("semantic_vad", turnDetection.GetProperty("type").GetString());
        Assert.Equal("low", turnDetection.GetProperty("eagerness").GetString());
        Assert.False(turnDetection.GetProperty("interrupt_response").GetBoolean());
        Assert.False(turnDetection.TryGetProperty("silence_duration_ms", out _));
    }

    [Fact]
    public void WriteClientMessage_SessionUpdate_ServerVad_WritesSilenceAndThreshold()
    {
        var options = new RealtimeSessionOptions
        {
            VoiceActivityDetection = new VoiceActivityDetectionOptions { Enabled = true, AllowInterruption = true },
            RawRepresentationFactory = () => new CrestApps.Core.AI.Realtime.RealtimeTurnDetectionOverrides
            {
                Type = CrestApps.Core.AI.Realtime.RealtimeTurnDetectionTypes.ServerVad,
                SilenceDurationMs = 900,
                Threshold = 0.6f,
            },
        };

        var json = WriteToJson(new SessionUpdateRealtimeClientMessage(options));

        var turnDetection = json.GetProperty("session").GetProperty("audio").GetProperty("input").GetProperty("turn_detection");
        Assert.Equal("server_vad", turnDetection.GetProperty("type").GetString());
        Assert.Equal(900, turnDetection.GetProperty("silence_duration_ms").GetInt32());
        Assert.False(turnDetection.TryGetProperty("eagerness", out _));
    }

    [Fact]
    public void WriteClientMessage_SessionUpdate_DisabledVad_WritesNullTurnDetection()
    {
        var options = new RealtimeSessionOptions
        {
            VoiceActivityDetection = new VoiceActivityDetectionOptions { Enabled = false },
        };

        var json = WriteToJson(new SessionUpdateRealtimeClientMessage(options));

        Assert.Equal(JsonValueKind.Null, json.GetProperty("session").GetProperty("audio").GetProperty("input").GetProperty("turn_detection").ValueKind);
    }

    [Fact]
    public void WriteClientMessage_InputAudioAppend_EncodesAudioAsBase64()
    {
        var audio = new byte[] { 1, 2, 3 };
        var message = new InputAudioBufferAppendRealtimeClientMessage(new DataContent(audio, "audio/pcm"));

        var json = WriteToJson(message);

        Assert.Equal("input_audio_buffer.append", json.GetProperty("type").GetString());
        Assert.Equal(Convert.ToBase64String(audio), json.GetProperty("audio").GetString());
    }

    [Fact]
    public void WriteClientMessage_InputAudioCommit_EmitsCommitEvent()
    {
        var json = WriteToJson(new InputAudioBufferCommitRealtimeClientMessage());

        Assert.Equal("input_audio_buffer.commit", json.GetProperty("type").GetString());
    }

    [Fact]
    public void WriteClientMessage_ResponseCreate_EmitsResponseCreateEvent()
    {
        var message = new CreateResponseRealtimeClientMessage
        {
            Instructions = "Reply in French.",
            OutputModalities = ["audio"],
        };

        var json = WriteToJson(message);

        Assert.Equal("response.create", json.GetProperty("type").GetString());
        Assert.Equal("Reply in French.", json.GetProperty("response").GetProperty("instructions").GetString());
    }

    // ---- AzureRealtimeProtocol: JSON -> server message ----

    [Fact]
    public void ReadServerMessage_AudioDelta_MapsToOutputAudioDelta()
    {
        var message = ReadFromJson("""{"type":"response.audio.delta","event_id":"e1","response_id":"r1","item_id":"i1","output_index":0,"content_index":0,"delta":"AQID"}""");

        var audio = Assert.IsType<OutputTextAudioRealtimeServerMessage>(message);
        Assert.Equal(RealtimeServerMessageType.OutputAudioDelta, audio.Type);
        Assert.Equal("AQID", audio.Audio);
        Assert.Equal("r1", audio.ResponseId);
        Assert.Equal("i1", audio.ItemId);
        Assert.Equal("e1", audio.MessageId);
    }

    [Fact]
    public void ReadServerMessage_GaAudioDeltaName_AlsoMapsToOutputAudioDelta()
    {
        var message = ReadFromJson("""{"type":"response.output_audio.delta","delta":"AQID"}""");

        var audio = Assert.IsType<OutputTextAudioRealtimeServerMessage>(message);
        Assert.Equal(RealtimeServerMessageType.OutputAudioDelta, audio.Type);
    }

    [Fact]
    public void ReadServerMessage_TranscriptDelta_MapsText()
    {
        var message = ReadFromJson("""{"type":"response.audio_transcript.delta","delta":"hello"}""");

        var audio = Assert.IsType<OutputTextAudioRealtimeServerMessage>(message);
        Assert.Equal(RealtimeServerMessageType.OutputAudioTranscriptionDelta, audio.Type);
        Assert.Equal("hello", audio.Text);
    }

    [Fact]
    public void ReadServerMessage_InputTranscriptionCompleted_MapsTranscription()
    {
        var message = ReadFromJson("""{"type":"conversation.item.input_audio_transcription.completed","item_id":"i9","transcript":"hi there"}""");

        var transcription = Assert.IsType<InputAudioTranscriptionRealtimeServerMessage>(message);
        Assert.Equal(RealtimeServerMessageType.InputAudioTranscriptionCompleted, transcription.Type);
        Assert.Equal("hi there", transcription.Transcription);
        Assert.Equal("i9", transcription.ItemId);
    }

    [Fact]
    public void ReadServerMessage_Error_MapsErrorContent()
    {
        var message = ReadFromJson("""{"type":"error","error":{"message":"bad request","code":"invalid"}}""");

        var error = Assert.IsType<ErrorRealtimeServerMessage>(message);
        Assert.Equal(RealtimeServerMessageType.Error, error.Type);
        Assert.Equal("bad request", error.Error?.Message);
        Assert.Equal("invalid", error.Error?.ErrorCode);
    }

    [Fact]
    public void ReadServerMessage_FunctionCallItem_MapsFunctionCallContent()
    {
        var message = ReadFromJson("""{"type":"response.output_item.done","response_id":"r1","output_index":0,"item":{"id":"item1","type":"function_call","call_id":"call_1","name":"get_weather","arguments":"{\"city\":\"Paris\"}"}}""");

        var output = Assert.IsType<ResponseOutputItemRealtimeServerMessage>(message);
        Assert.Equal(RealtimeServerMessageType.ResponseOutputItemDone, output.Type);
        var call = Assert.IsType<FunctionCallContent>(Assert.Single(output.Item!.Contents));
        Assert.Equal("get_weather", call.Name);
        Assert.Equal("call_1", call.CallId);
    }

    [Fact]
    public void ReadServerMessage_UnknownEvent_MapsToRawContentOnly()
    {
        var message = ReadFromJson("""{"type":"rate_limits.updated","event_id":"e2"}""");

        Assert.Equal(RealtimeServerMessageType.RawContentOnly, message.Type);
        Assert.Equal("e2", message.MessageId);
        Assert.IsType<JsonElement>(message.RawRepresentation);
    }

    // ---- AzureRealtimeClientSession over a fake socket ----

    [Fact]
    public async Task Session_SendAsync_WritesFrameToSocket()
    {
        var socket = new FakeWebSocket();
        await using var session = new AzureRealtimeClientSession(socket, options: null);

        await session.SendAsync(new InputAudioBufferCommitRealtimeClientMessage(), TestContext.Current.CancellationToken);

        var sent = Assert.Single(socket.SentMessages);
        using var document = JsonDocument.Parse(sent);
        Assert.Equal("input_audio_buffer.commit", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Session_GetStreamingResponseAsync_YieldsMappedMessagesThenStopsOnClose()
    {
        var socket = new FakeWebSocket();
        socket.EnqueueIncoming("""{"type":"response.audio.delta","delta":"AQID"}""");
        socket.EnqueueIncoming("""{"type":"error","error":{"message":"boom"}}""");
        await using var session = new AzureRealtimeClientSession(socket, options: null);

        var received = new List<RealtimeServerMessage>();
        await foreach (var message in session.GetStreamingResponseAsync(TestContext.Current.CancellationToken))
        {
            received.Add(message);
        }

        Assert.Equal(2, received.Count);
        Assert.IsType<OutputTextAudioRealtimeServerMessage>(received[0]);
        Assert.IsType<ErrorRealtimeServerMessage>(received[1]);
    }

    // ---- AzureRealtimeClient connection + session bootstrap ----

    [Fact]
    public async Task Client_CreateSessionAsync_ConnectsToAzureUrl_WithAuthHeader_AndSendsSessionUpdate()
    {
        Uri? capturedUri = null;
        IReadOnlyList<KeyValuePair<string, string>>? capturedHeaders = null;
        var socket = new FakeWebSocket();

        var client = new AzureRealtimeClient(
            endpoint: new Uri("https://my-resource.openai.azure.com/"),
            deployment: "gpt-realtime",
            authHeaderFactory: _ => new ValueTask<KeyValuePair<string, string>>(new KeyValuePair<string, string>("api-key", "secret")),
            connect: (uri, headers, _) =>
            {
                capturedUri = uri;
                capturedHeaders = headers;
                return new ValueTask<WebSocket>(socket);
            });

        var options = new RealtimeSessionOptions { Instructions = "hi" };
        await using var session = await client.CreateSessionAsync(options, TestContext.Current.CancellationToken);

        Assert.Equal("wss", capturedUri!.Scheme);
        Assert.Equal("my-resource.openai.azure.com", capturedUri.Host);
        Assert.Equal("/openai/v1/realtime", capturedUri.AbsolutePath);
        Assert.Contains("model=gpt-realtime", capturedUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("api-version", capturedUri.Query, StringComparison.Ordinal);
        Assert.Contains(new KeyValuePair<string, string>("api-key", "secret"), capturedHeaders!);

        // The session is bootstrapped with a session.update carrying the options.
        var sent = Assert.Single(socket.SentMessages);
        using var document = JsonDocument.Parse(sent);
        Assert.Equal("session.update", document.RootElement.GetProperty("type").GetString());
    }

    // ---- AzureRealtimeClientFactory ----

    [Fact]
    public void Factory_ApiKeyConnection_BuildsClient()
    {
        var connection = new AIProviderConnectionEntry(new Dictionary<string, object>
        {
            ["Endpoint"] = "https://my-resource.openai.azure.com/",
            ["ApiKey"] = "secret",
            ["AuthenticationType"] = "ApiKey",
        });

        var client = AzureRealtimeClientFactory.Create(connection, "gpt-4o-realtime-preview");

        Assert.NotNull(client);
        Assert.IsType<AzureRealtimeClient>(client);
    }

    [Fact]
    public void Factory_WithoutDeployment_Throws()
    {
        var connection = new AIProviderConnectionEntry(new Dictionary<string, object>
        {
            ["Endpoint"] = "https://my-resource.openai.azure.com/",
            ["ApiKey"] = "secret",
            ["AuthenticationType"] = "ApiKey",
        });

        Assert.Throws<ArgumentException>(() => AzureRealtimeClientFactory.Create(connection, deploymentName: null!));
    }

    private static JsonElement WriteToJson(RealtimeClientMessage message)
    {
        var bytes = AzureRealtimeProtocol.WriteClientMessage(message);
        Assert.NotNull(bytes);
        using var document = JsonDocument.Parse(bytes!.Value);
        return document.RootElement.Clone();
    }

    private static RealtimeServerMessage ReadFromJson(string json)
        => AzureRealtimeProtocol.ReadServerMessage(Encoding.UTF8.GetBytes(json));

    private sealed class FakeWebSocket : WebSocket
    {
        private readonly Queue<byte[]> _incoming = new();
        private readonly List<byte[]> _sent = [];
        private readonly List<byte> _pendingSend = [];
        private WebSocketState _state = WebSocketState.Open;

        public IReadOnlyList<byte[]> SentMessages => _sent;

        public void EnqueueIncoming(string json) => _incoming.Enqueue(Encoding.UTF8.GetBytes(json));

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => _state;

        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_incoming.Count == 0)
            {
                _state = WebSocketState.Closed;
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, endOfMessage: true));
            }

            var message = _incoming.Dequeue();
            var count = Math.Min(message.Length, buffer.Count);
            Array.Copy(message, 0, buffer.Array!, buffer.Offset, count);

            return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, endOfMessage: true));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            _pendingSend.AddRange(buffer);
            if (endOfMessage)
            {
                _sent.Add([.. _pendingSend]);
                _pendingSend.Clear();
            }

            return Task.CompletedTask;
        }
    }
}
