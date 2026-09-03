#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
#nullable enable
using System.Runtime.CompilerServices;
using System.Text.Json;
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default <see cref="IRealtimeConversation"/> backed by an <see cref="IRealtimeClientSession"/> (typically
/// the function-invoking session from Microsoft.Extensions.AI). It appends microphone audio to the input
/// buffer and translates provider realtime server messages into provider-neutral
/// <see cref="RealtimeConversationEvent"/>s. Because the underlying session's streaming enumeration also
/// drives the realtime tool loop, callers must enumerate <see cref="GetEventsAsync"/> within their
/// <c>AIInvocationScope</c>.
/// </summary>
internal sealed class DefaultRealtimeConversation : IRealtimeConversation
{
    private readonly IRealtimeClientSession _session;
    private int _disposed;

    public DefaultRealtimeConversation(IRealtimeClientSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <inheritdoc />
    public Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken = default)
    {
        return _session.SendAsync(new InputAudioBufferAppendRealtimeClientMessage(new DataContent(audio, "audio/pcm")), cancellationToken);
    }

    /// <inheritdoc />
    public Task TruncateAssistantAudioAsync(string itemId, int audioEndMs, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(itemId);

        // There is no typed client message for this in the Microsoft.Extensions.AI realtime model, so it goes out
        // as a raw event. Both transports forward an unrecognised message's raw representation verbatim.
        var payload = JsonSerializer.Serialize(new
        {
            type = "conversation.item.truncate",
            item_id = itemId,
            content_index = 0,
            audio_end_ms = Math.Max(0, audioEndMs),
        });

        return _session.SendAsync(new RealtimeClientMessage { RawRepresentation = payload }, cancellationToken);
    }

    /// <inheritdoc />
    public Task UpdateTurnDetectionAsync(
        bool allowInterruption,
        int? silenceDurationMs,
        float? vadThreshold,
        CancellationToken cancellationToken = default)
    {
        var options = _session.Options;

        if (options is null)
        {
            return Task.CompletedTask;
        }

        // Re-send the session options with new turn detection. The silence/threshold knobs have no place in the
        // Microsoft.Extensions.AI options model, so they ride the raw representation exactly as they do when the
        // session is first configured (see DefaultRealtimeSessionConfigurator).
        var overrides = new RealtimeTurnDetectionOverrides
        {
            SilenceDurationMs = silenceDurationMs,
            Threshold = vadThreshold,
        };

        // RealtimeSessionOptions is init-only, so the update carries a copy of the session as configured with only
        // the turn-detection parts replaced. Everything else must be repeated verbatim: a session.update replaces
        // the session's configuration, so omitting instructions or tools here would quietly strip them mid-call.
        var updated = new RealtimeSessionOptions
        {
            Model = options.Model,
            Instructions = options.Instructions,
            Voice = options.Voice,
            MaxOutputTokens = options.MaxOutputTokens,
            InputAudioFormat = options.InputAudioFormat,
            OutputAudioFormat = options.OutputAudioFormat,
            OutputModalities = options.OutputModalities,
            TranscriptionOptions = options.TranscriptionOptions,
            Tools = options.Tools,
            ToolMode = options.ToolMode,
            RawRepresentationFactory = overrides.HasValues ? (() => overrides) : null,
            VoiceActivityDetection = new VoiceActivityDetectionOptions
            {
                Enabled = true,
                AllowInterruption = allowInterruption,
            },
        };

        return _session.SendAsync(new SessionUpdateRealtimeClientMessage(updated), cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RealtimeConversationEvent> GetEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _session.GetStreamingResponseAsync(cancellationToken))
        {
            var mapped = Map(message);

            if (mapped is not null)
            {
                yield return mapped;
            }
        }
    }

    private static RealtimeConversationEvent? Map(RealtimeServerMessage message)
    {
        switch (message)
        {
            case OutputTextAudioRealtimeServerMessage audio
                when audio.Type == RealtimeServerMessageType.OutputAudioDelta && !string.IsNullOrEmpty(audio.Audio):
                return new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.AssistantAudioDelta,
                    Audio = Convert.FromBase64String(audio.Audio),
                    ItemId = audio.ItemId,
                    ResponseId = audio.ResponseId,
                };

            case OutputTextAudioRealtimeServerMessage transcript
                when transcript.Type == RealtimeServerMessageType.OutputAudioTranscriptionDelta && !string.IsNullOrEmpty(transcript.Text):
                return new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.AssistantTranscriptDelta,
                    Text = transcript.Text,
                    ItemId = transcript.ItemId,
                    ResponseId = transcript.ResponseId,
                };

            case OutputTextAudioRealtimeServerMessage transcript
                when transcript.Type == RealtimeServerMessageType.OutputAudioTranscriptionDone && !string.IsNullOrEmpty(transcript.Text):
                return new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.AssistantTranscriptDone,
                    Text = transcript.Text,
                    ItemId = transcript.ItemId,
                    ResponseId = transcript.ResponseId,
                };

            case InputAudioTranscriptionRealtimeServerMessage userTranscript
                when userTranscript.Type == RealtimeServerMessageType.InputAudioTranscriptionCompleted && !string.IsNullOrEmpty(userTranscript.Transcription):
                return new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.UserTranscript,
                    Text = userTranscript.Transcription,
                    ItemId = userTranscript.ItemId,
                };

            // Transcription can fail (unintelligible audio, provider error). No UserTranscript follows, so the
            // failure must still be surfaced or per-utterance bookkeeping downstream drifts by one turn.
            case InputAudioTranscriptionRealtimeServerMessage failedTranscript
                when failedTranscript.Type == RealtimeServerMessageType.InputAudioTranscriptionFailed:
                return new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.UserTranscriptFailed,
                    ErrorMessage = failedTranscript.Error?.Message,
                    ItemId = failedTranscript.ItemId,
                };

            case ErrorRealtimeServerMessage error:
                return new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.Error,
                    ErrorMessage = error.Error?.Message ?? "Unknown realtime error.",
                };

            case ResponseCreatedRealtimeServerMessage response
                when response.Type == RealtimeServerMessageType.ResponseCreated:
                return new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.ResponseStarted,
                    ResponseId = response.ResponseId,
                };

            case ResponseCreatedRealtimeServerMessage response
                when response.Type == RealtimeServerMessageType.ResponseDone:
                return new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.ResponseCompleted,
                    ResponseStatus = response.Status,
                    ResponseId = response.ResponseId,
                    // A response can end as failed/incomplete (rate limit, content filter, token cap). The reason
                    // lives only in the raw payload, so read it here and let the runner decide what to surface.
                    ErrorMessage = ReadResponseFailureReason(message.RawRepresentation),
                };

            default:
                return MapRawTurnSignal(message);
        }
    }

    // Reads response.status_details.error.message from a raw response.done payload, when present.
    private static string? ReadResponseFailureReason(object? rawRepresentation)
    {
        if (rawRepresentation is not JsonElement raw || raw.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!raw.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Object ||
            !response.TryGetProperty("status_details", out var details) || details.ValueKind != JsonValueKind.Object ||
            !details.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object ||
            !error.TryGetProperty("message", out var errorMessage))
        {
            return null;
        }

        return errorMessage.GetString();
    }

    // Two turn-taking signals have no typed message in the Microsoft.Extensions.AI model: the user starting to
    // speak, and the provider committing their utterance as a conversation item. Both matter — the first drives
    // barge-in, the second is what pairs an utterance with the transcript that arrives for it later.
    //
    // The custom Azure transport keeps the raw JSON, while Microsoft.Extensions.AI's OpenAI client keeps an SDK
    // object instead — so match on the raw JSON when we have it, and on the SDK update's type name when we don't.
    // Without the second branch, barge-in flush and partial-turn persistence silently do nothing on OpenAI-direct
    // deployments.
    private static RealtimeConversationEvent? MapRawTurnSignal(RealtimeServerMessage message)
    {
        if (message.Type != RealtimeServerMessageType.RawContentOnly || message.RawRepresentation is null)
        {
            return null;
        }

        if (message.RawRepresentation is JsonElement raw)
        {
            if (raw.ValueKind != JsonValueKind.Object ||
                !raw.TryGetProperty("type", out var rawType))
            {
                return null;
            }

            return rawType.GetString() switch
            {
                "input_audio_buffer.speech_started" => new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.UserSpeechStarted,
                    ItemId = ReadString(raw, "item_id"),
                },
                "input_audio_buffer.committed" => new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.UserTurnCommitted,
                    ItemId = ReadString(raw, "item_id"),
                },
                _ => null,
            };
        }

        // OpenAI SDK update types, e.g. InputAudioSpeechStartedUpdate / InputAudioCommittedUpdate.
        var typeName = message.RawRepresentation.GetType().Name;

        if (typeName.Contains("SpeechStarted", StringComparison.Ordinal))
        {
            return new RealtimeConversationEvent
            {
                Type = RealtimeConversationEventType.UserSpeechStarted,
                ItemId = ReadSdkItemId(message.RawRepresentation),
            };
        }

        if (typeName.Contains("Committed", StringComparison.Ordinal))
        {
            return new RealtimeConversationEvent
            {
                Type = RealtimeConversationEventType.UserTurnCommitted,
                ItemId = ReadSdkItemId(message.RawRepresentation),
            };
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // The SDK update objects expose the item id as a property; read it reflectively rather than taking a
    // compile-time dependency on the OpenAI SDK from this assembly. A missing id is survivable: the runner falls
    // back to arrival order, which is what it did before item ids existed at all.
    private static string? ReadSdkItemId(object rawRepresentation)
    {
        try
        {
            var property = rawRepresentation.GetType().GetProperty("ItemId");

            return property?.GetValue(rawRepresentation) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _session.DisposeAsync();
    }
}
