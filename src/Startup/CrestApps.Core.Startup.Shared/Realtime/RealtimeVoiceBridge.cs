#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
#nullable enable
using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Realtime;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Startup.Shared.Realtime;

/// <summary>
/// Shared server-side bridge for the realtime (speech-to-speech) test harness used by both the MVC and Blazor
/// sample hosts. It accepts a browser WebSocket, opens a provider realtime session from a
/// <see cref="AIDeploymentPurpose.Realtime"/> deployment, and relays binary PCM16 audio in both directions while
/// forwarding transcripts, errors, and turn events as JSON text frames.
/// </summary>
public static class RealtimeVoiceBridge
{
    private const int ReceiveBufferSize = 32 * 1024;

    /// <summary>
    /// Accepts the incoming WebSocket on <paramref name="httpContext"/> and runs the realtime bridge for the
    /// requested deployment. Any resolution or connection failure is reported to the page as an error frame.
    /// </summary>
    public static async Task HandleAsync(
        HttpContext httpContext,
        string? deploymentName,
        string? voice,
        string? instructions,
        IAIDeploymentManager deploymentManager,
        IAIClientFactory clientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!httpContext.WebSockets.IsWebSocketRequest)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        using var socket = await httpContext.WebSockets.AcceptWebSocketAsync();

        try
        {
            var deployment = await deploymentManager.ResolveOrDefaultAsync(AIDeploymentPurpose.Realtime, deploymentName, cancellationToken: cancellationToken);

            if (deployment is null)
            {
                await SendJsonAsync(socket, new { type = "error", message = "No realtime deployment could be resolved. Create an AI deployment whose purpose includes 'Realtime'." }, cancellationToken);

                return;
            }

            await BridgeAsync(socket, deployment, voice, instructions, clientFactory, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The client disconnected or the request was aborted.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Realtime bridge failed for deployment '{DeploymentName}'.", deploymentName);
            await TrySendJsonAsync(socket, new { type = "error", message = $"{exception.GetType().Name}: {exception.Message}" }, CancellationToken.None);
        }
        finally
        {
            await TryCloseAsync(socket);
        }
    }

    /// <summary>
    /// Accepts the incoming WebSocket and runs the realtime bridge through the AI orchestrator for a chat
    /// profile whose chat mode is <see cref="ChatMode.Realtime"/>, so the session honors the profile's
    /// system message, tools, and knowledge base (RAG via the search tool) — not just a raw instruction string.
    /// </summary>
    public static async Task HandleProfileAsync(
        HttpContext httpContext,
        AIProfile? profile,
        string? voice,
        string? language,
        IRealtimeOrchestrator orchestrator,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!httpContext.WebSockets.IsWebSocketRequest)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        using var socket = await httpContext.WebSockets.AcceptWebSocketAsync();

        try
        {
            if (profile is null)
            {
                await SendJsonAsync(socket, new { type = "error", message = "The selected profile was not found." }, cancellationToken);

                return;
            }

            if (!profile.TryGetSettings<ChatModeProfileSettings>(out var chatModeSettings) || chatModeSettings.ChatMode != ChatMode.Realtime)
            {
                await SendJsonAsync(socket, new { type = "error", message = "The selected profile does not have realtime chat mode enabled." }, cancellationToken);

                return;
            }

            // Keep the invocation scope open for the whole conversation so tools invoked mid-session
            // observe the correct ambient context. The orchestrator populates it during StartAsync.
            using var scope = AIInvocationScope.Begin();

            await using var conversation = await orchestrator.StartAsync(
                new RealtimeOrchestrationRequest
                {
                    Resource = profile,
                    RealtimeDeploymentName = profile.RealtimeDeploymentName,
                    Voice = voice,
                    SpeechLanguage = language,
                },
                cancellationToken);

            await SendJsonAsync(socket, new { type = "ready", deployment = profile.Name }, cancellationToken);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var fromModel = PumpConversationToBrowserAsync(socket, conversation, linkedCts.Token);
            var fromBrowser = PumpBrowserToConversationAsync(socket, conversation, linkedCts.Token);

            await Task.WhenAny(fromModel, fromBrowser);
            await linkedCts.CancelAsync();

            try
            {
                await Task.WhenAll(fromModel, fromBrowser);
            }
            catch (OperationCanceledException)
            {
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Realtime profile bridge failed for profile '{ProfileName}'.", profile?.Name);
            await TrySendJsonAsync(socket, new { type = "error", message = $"{exception.GetType().Name}: {exception.Message}" }, CancellationToken.None);
        }
        finally
        {
            await TryCloseAsync(socket);
        }
    }

    private static async Task PumpConversationToBrowserAsync(WebSocket socket, IRealtimeConversation conversation, CancellationToken cancellationToken)
    {
        await foreach (var evt in conversation.GetEventsAsync(cancellationToken))
        {
            switch (evt.Type)
            {
                case RealtimeConversationEventType.AssistantAudioDelta when !evt.Audio.IsEmpty:
                    await socket.SendAsync(evt.Audio, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
                    break;

                case RealtimeConversationEventType.AssistantTranscriptDelta when !string.IsNullOrEmpty(evt.Text):
                    await SendJsonAsync(socket, new { type = "transcript", role = "assistant", text = evt.Text }, cancellationToken);
                    break;

                case RealtimeConversationEventType.UserTranscript when !string.IsNullOrEmpty(evt.Text):
                    await SendJsonAsync(socket, new { type = "transcript", role = "user", text = evt.Text }, cancellationToken);
                    break;

                case RealtimeConversationEventType.UserSpeechStarted:
                    await SendJsonAsync(socket, new { type = "event", name = "speech_started" }, cancellationToken);
                    break;

                case RealtimeConversationEventType.Error:
                    await SendJsonAsync(socket, new { type = "error", message = evt.ErrorMessage ?? "Unknown realtime error." }, cancellationToken);
                    break;
            }
        }
    }

    private static async Task PumpBrowserToConversationAsync(WebSocket socket, IRealtimeConversation conversation, CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var accumulator = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(rented.AsMemory(), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    accumulator.Write(rented, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (accumulator.Length == 0 || result.MessageType != WebSocketMessageType.Binary)
                {
                    continue;
                }

                await conversation.SendAudioAsync(accumulator.ToArray(), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static async Task BridgeAsync(WebSocket socket, AIDeployment deployment, string? voice, string? instructions, IAIClientFactory clientFactory, CancellationToken cancellationToken)
    {
        var realtimeClient = await clientFactory.CreateRealtimeClientAsync(deployment);

        var options = new RealtimeSessionOptions
        {
            Model = deployment.ModelName,
            Instructions = string.IsNullOrWhiteSpace(instructions)
                ? "You are a friendly voice assistant. Keep your spoken answers short and conversational."
                : instructions,
            Voice = string.IsNullOrWhiteSpace(voice) ? "alloy" : voice,
            InputAudioFormat = new RealtimeAudioFormat("audio/pcm", 24000),
            OutputAudioFormat = new RealtimeAudioFormat("audio/pcm", 24000),

            // The realtime API accepts only a single output modality ("audio" or "text"), not both.
            // Audio output still emits a text transcript (response.output_audio_transcript.*), which drives the UI.
            OutputModalities = ["audio"],

            // Transcribe the user's input audio so their words also appear in the transcript.
            TranscriptionOptions = new TranscriptionOptions { ModelId = "whisper-1" },
            VoiceActivityDetection = new VoiceActivityDetectionOptions { Enabled = true, AllowInterruption = true },
        };

        await using var session = await realtimeClient.CreateSessionAsync(options, cancellationToken);

        await SendJsonAsync(socket, new { type = "ready", deployment = deployment.Name }, cancellationToken);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var fromModel = PumpModelToBrowserAsync(socket, session, linkedCts.Token);
        var fromBrowser = PumpBrowserToModelAsync(socket, session, linkedCts.Token);

        await Task.WhenAny(fromModel, fromBrowser);
        await linkedCts.CancelAsync();

        try
        {
            await Task.WhenAll(fromModel, fromBrowser);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task PumpModelToBrowserAsync(WebSocket socket, IRealtimeClientSession session, CancellationToken cancellationToken)
    {
        await foreach (var message in session.GetStreamingResponseAsync(cancellationToken))
        {
            switch (message)
            {
                case OutputTextAudioRealtimeServerMessage audio when audio.Type == RealtimeServerMessageType.OutputAudioDelta && audio.Audio is not null:
                    await socket.SendAsync(Convert.FromBase64String(audio.Audio), WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
                    break;

                case OutputTextAudioRealtimeServerMessage transcript when transcript.Type == RealtimeServerMessageType.OutputAudioTranscriptionDelta && !string.IsNullOrEmpty(transcript.Text):
                    await SendJsonAsync(socket, new { type = "transcript", role = "assistant", text = transcript.Text }, cancellationToken);
                    break;

                case InputAudioTranscriptionRealtimeServerMessage userTranscript when userTranscript.Type == RealtimeServerMessageType.InputAudioTranscriptionCompleted && !string.IsNullOrEmpty(userTranscript.Transcription):
                    await SendJsonAsync(socket, new { type = "transcript", role = "user", text = userTranscript.Transcription }, cancellationToken);
                    break;

                case ErrorRealtimeServerMessage error:
                    await SendJsonAsync(socket, new { type = "error", message = error.Error?.Message ?? "Unknown realtime error." }, cancellationToken);
                    break;

                default:
                    if (message.Type == RealtimeServerMessageType.RawContentOnly &&
                        message.RawRepresentation is JsonElement raw &&
                        raw.TryGetProperty("type", out var rawType) &&
                        rawType.GetString() is "input_audio_buffer.speech_started")
                    {
                        await SendJsonAsync(socket, new { type = "event", name = "speech_started" }, cancellationToken);
                    }

                    break;
            }
        }
    }

    private static async Task PumpBrowserToModelAsync(WebSocket socket, IRealtimeClientSession session, CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var accumulator = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(rented.AsMemory(), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    accumulator.Write(rented, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (accumulator.Length == 0)
                {
                    continue;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    var audio = accumulator.ToArray();
                    await session.SendAsync(new InputAudioBufferAppendRealtimeClientMessage(new DataContent(audio, "audio/pcm")), cancellationToken);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static async Task SendJsonAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    private static async Task TrySendJsonAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await SendJsonAsync(socket, payload, cancellationToken);
            }
        }
        catch (WebSocketException)
        {
        }
    }

    private static async Task TryCloseAsync(WebSocket socket)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }
}
