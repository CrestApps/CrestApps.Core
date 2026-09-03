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
                };

            case OutputTextAudioRealtimeServerMessage transcript
                when transcript.Type == RealtimeServerMessageType.OutputAudioTranscriptionDelta && !string.IsNullOrEmpty(transcript.Text):
                return new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.AssistantTranscriptDelta,
                    Text = transcript.Text,
                };

            case OutputTextAudioRealtimeServerMessage transcript
                when transcript.Type == RealtimeServerMessageType.OutputAudioTranscriptionDone && !string.IsNullOrEmpty(transcript.Text):
                return new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.AssistantTranscriptDone,
                    Text = transcript.Text,
                };

            case InputAudioTranscriptionRealtimeServerMessage userTranscript
                when userTranscript.Type == RealtimeServerMessageType.InputAudioTranscriptionCompleted && !string.IsNullOrEmpty(userTranscript.Transcription):
                return new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.UserTranscript,
                    Text = userTranscript.Transcription,
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
                };

            case ResponseCreatedRealtimeServerMessage response
                when response.Type == RealtimeServerMessageType.ResponseDone:
                return new RealtimeConversationEvent
                {
                    Type = RealtimeConversationEventType.ResponseCompleted,
                    ResponseStatus = response.Status,
                };

            default:
                if (message.Type == RealtimeServerMessageType.RawContentOnly &&
                    message.RawRepresentation is JsonElement raw &&
                    raw.ValueKind == JsonValueKind.Object &&
                    raw.TryGetProperty("type", out var rawType) &&
                    rawType.GetString() is "input_audio_buffer.speech_started")
                {
                    return new RealtimeConversationEvent
                    {
                        Type = RealtimeConversationEventType.UserSpeechStarted,
                    };
                }

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
