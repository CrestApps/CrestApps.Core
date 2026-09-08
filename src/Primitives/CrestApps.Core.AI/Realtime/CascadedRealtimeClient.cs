#pragma warning disable MEAI001 // The realtime types from Microsoft.Extensions.AI are for evaluation purposes only.
#nullable enable
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Realtime;

/// <summary>
/// An <see cref="IRealtimeClient"/> that answers speech with speech without a speech-to-speech model, by
/// chaining a realtime transcription client, a chat client, and a text-to-speech client.
/// </summary>
/// <remarks>
/// This is what lets a provider that only transcribes — or a host that wants to reason with one vendor and
/// speak with another — serve the realtime experience. It is composed by <c>IAIClientFactory</c> from a
/// deployment carrying <see cref="CascadedRealtimeMetadata"/>, because a single
/// <c>IAIClientProvider</c> only ever sees one connection and so could never reach three deployments.
/// </remarks>
public sealed class CascadedRealtimeClient : IRealtimeClient
{
    private readonly IRealtimeClient _transcriptionClient;
    private readonly IChatClient _chatClient;
    private readonly ITextToSpeechClient _speechClient;
    private readonly string? _speechModelId;
    private readonly ILogger<CascadedRealtimeClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CascadedRealtimeClient"/> class.
    /// </summary>
    /// <param name="transcriptionClient">The realtime client that transcribes the user's speech.</param>
    /// <param name="chatClient">The chat client that generates the reply. Supply one that already has function invocation applied when the session should be able to call tools.</param>
    /// <param name="speechClient">The text-to-speech client that speaks the reply.</param>
    /// <param name="speechModelId">The model the text-to-speech deployment should use, or <see langword="null"/> for its default.</param>
    /// <param name="logger">The logger.</param>
    public CascadedRealtimeClient(
        IRealtimeClient transcriptionClient,
        IChatClient chatClient,
        ITextToSpeechClient speechClient,
        string? speechModelId,
        ILogger<CascadedRealtimeClient> logger)
    {
        _transcriptionClient = transcriptionClient;
        _chatClient = chatClient;
        _speechClient = speechClient;
        _speechModelId = speechModelId;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IRealtimeClientSession> CreateSessionAsync(RealtimeSessionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var transcription = await _transcriptionClient.CreateSessionAsync(BuildTranscriptionOptions(options), cancellationToken);

        try
        {
            var session = new CascadedRealtimeSession(
                transcription,
                _chatClient,
                _speechClient,
                options,
                BuildChatOptions(options),
                BuildSpeechOptions(options),
                _logger);

            session.Start();

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Started a cascaded realtime session speaking with voice '{Voice}'.", options?.Voice);
            }

            return session;
        }
        catch
        {
            await transcription.DisposeAsync();

            throw;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        return _transcriptionClient.GetService(serviceType)
            ?? _chatClient.GetService(serviceType)
            ?? _speechClient.GetService(serviceType);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The legs are handed to each session, which disposes them when it ends. Disposing them here would
        // tear down a session that is still running.
        _transcriptionClient.Dispose();
    }

    /// <summary>
    /// Builds the options for the transcription leg. Only the input audio and transcription settings carry
    /// over: the reply is produced by the chat leg, not by the transcriber.
    /// </summary>
    /// <param name="options">The session options the caller requested.</param>
    private static RealtimeSessionOptions BuildTranscriptionOptions(RealtimeSessionOptions? options)
    {
        return new RealtimeSessionOptions
        {
            SessionKind = RealtimeSessionKind.Transcription,
            InputAudioFormat = options?.InputAudioFormat,
            TranscriptionOptions = options?.TranscriptionOptions,
            VoiceActivityDetection = options?.VoiceActivityDetection,
            RawRepresentationFactory = options?.RawRepresentationFactory,
        };
    }

    /// <summary>
    /// Builds the options applied to every chat turn, carrying across the tools and limits the orchestrator
    /// resolved for the conversation.
    /// </summary>
    /// <param name="options">The session options the caller requested.</param>
    private static ChatOptions? BuildChatOptions(RealtimeSessionOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        return new ChatOptions
        {
            MaxOutputTokens = options.MaxOutputTokens,
            Tools = options.Tools?.ToList(),
            ToolMode = options.ToolMode,
        };
    }

    /// <summary>
    /// Builds the options applied to every spoken fragment, pinning the audio format to the one the caller
    /// asked the session to produce.
    /// </summary>
    /// <param name="options">The session options the caller requested.</param>
    private TextToSpeechOptions BuildSpeechOptions(RealtimeSessionOptions? options)
    {
        return new TextToSpeechOptions
        {
            ModelId = _speechModelId,
            VoiceId = options?.Voice,
            Language = options?.TranscriptionOptions?.SpeechLanguage,
            AudioFormat = FormatAudioFormat(options?.OutputAudioFormat),
        };
    }

    /// <summary>
    /// Names the speech client's output format so it emits the raw PCM the realtime pipeline plays, at the
    /// rate the session negotiated. Providers parse this name themselves, so an unknown rate falls through
    /// to the provider's own default rather than failing the session.
    /// </summary>
    /// <param name="format">The output audio format the caller requested.</param>
    private static string? FormatAudioFormat(RealtimeAudioFormat? format)
    {
        if (format is null || format.SampleRate <= 0)
        {
            return null;
        }

        return $"Pcm{format.SampleRate}";
    }
}
