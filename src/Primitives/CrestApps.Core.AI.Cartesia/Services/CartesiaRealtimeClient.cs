#pragma warning disable MEAI001 // Speech and realtime APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using CrestApps.Core.AI.Realtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.AI.Cartesia.Services;

/// <summary>
/// Cartesia's realtime speech-to-text, as the speech-to-text leg of a cascaded realtime deployment.
/// </summary>
/// <remarks>
/// <para>
/// Cartesia's turns endpoint (<c>wss://api.cartesia.ai/stt/turns/websocket</c>) transcribes a stream of audio and decides
/// on the server where each of the caller's turns ends, which is what a cascade needs from its speech-to-text leg. Only
/// transcription sessions are opened: Cartesia does not hold a conversation.
/// </para>
/// <para>
/// The session decides what it can, as it does for a speech-to-speech provider. The model is the session's
/// transcription model, or else the deployment's; a model the turns endpoint does not offer, such as the batch
/// <c>ink-whisper</c>, gives way to <see cref="CartesiaConstants.DefaultRealtimeSpeechToTextModel"/>. A silence the
/// session's turn detection names caps how long Cartesia waits after speech before it ends a turn. The endpoint takes no
/// language: it detects the languages it transcribes on its own.
/// </para>
/// </remarks>
public sealed class CartesiaRealtimeClient : IRealtimeClient
{
    /// <summary>
    /// How long Cartesia may wait after speech stops before it ends a turn, when the session names no silence. Cartesia's
    /// own default, 5600 ms, kept callers waiting for an answer; 1200 ms is what worked on calls.
    /// </summary>
    internal const int DefaultTurnEndTimeoutMilliseconds = 1200;

    private const int MinimumTurnEndTimeoutMilliseconds = 640;
    private const int MaximumTurnEndTimeoutMilliseconds = 11200;
    private const int DefaultSampleRate = 24000;

    private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);

    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly string _model;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CartesiaRealtimeClient"/> class.
    /// </summary>
    /// <param name="apiKey">The API key.</param>
    /// <param name="endpoint">The endpoint, or <see langword="null"/> for Cartesia's own.</param>
    /// <param name="model">The deployment's model, or <see langword="null"/>.</param>
    /// <param name="logger">The logger, or <see langword="null"/> for none.</param>
    internal CartesiaRealtimeClient(string apiKey, Uri endpoint, string model, ILogger logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _apiKey = apiKey;
        _endpoint = endpoint;
        _model = model;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    public async Task<IRealtimeClientSession> CreateSessionAsync(RealtimeSessionOptions options = null, CancellationToken cancellationToken = default)
    {
        if (options?.SessionKind != RealtimeSessionKind.Transcription)
        {
            throw new NotSupportedException("Cartesia only transcribes speech in realtime. Use it as the speech-to-text deployment of a cascaded realtime deployment.");
        }

        var format = options.InputAudioFormat;
        var sampleRate = format?.SampleRate > 0 ? format.SampleRate : DefaultSampleRate;
        var encoding = GetEncoding(format?.MediaType);
        var model = GetModel(options);
        var turnEndTimeout = GetTurnEndTimeout(options);
        var address = BuildAddress(_endpoint, model, encoding, sampleRate, turnEndTimeout);

        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("X-API-Key", _apiKey);

        // The status Cartesia refuses the connection with is what tells a wrong key from a model or format it does not
        // offer, and the socket keeps it only when asked to.
        socket.Options.CollectHttpResponseDetails = true;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_connectTimeout);

        try
        {
            await socket.ConnectAsync(address, timeout.Token);
        }
        catch (WebSocketException exception) when (RefusalMessage(socket.HttpStatusCode) is { } message)
        {
            socket.Dispose();

            throw new InvalidOperationException(message, exception);
        }
        catch
        {
            socket.Dispose();

            throw;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Opened a Cartesia transcription session: model {Model}, {Encoding} at {SampleRate} Hz, turns end at most {TurnEndTimeout} ms after speech.",
                model, encoding, sampleRate, turnEndTimeout);
        }

        var bytesPerSample = encoding == "pcm_s16le" ? 2 : 1;

        return new CartesiaTranscriptionSession(socket, options, sampleRate * bytesPerSample, turnEndTimeout, _logger);
    }

    /// <inheritdoc/>
    public object GetService(Type serviceType, object serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Each session owns its own connection; the client holds nothing to release.
    }

    /// <summary>
    /// The address of the turns endpoint for one session.
    /// </summary>
    /// <param name="endpoint">The endpoint, or <see langword="null"/> for Cartesia's own.</param>
    /// <param name="model">The speech-to-text model.</param>
    /// <param name="encoding">Cartesia's name for the audio's encoding.</param>
    /// <param name="sampleRate">The audio's sample rate.</param>
    /// <param name="turnEndTimeoutMilliseconds">How long Cartesia may wait after speech before it ends a turn.</param>
    internal static Uri BuildAddress(Uri endpoint, string model, string encoding, int sampleRate, int turnEndTimeoutMilliseconds)
    {
        var baseUri = endpoint ?? new Uri(CartesiaConstants.DefaultEndpoint);
        var scheme = baseUri.Scheme switch
        {
            "https" => "wss",
            "http" => "ws",
            _ => baseUri.Scheme,
        };

        var query = string.Join('&',
            "model=" + Uri.EscapeDataString(model),
            "encoding=" + encoding,
            "sample_rate=" + sampleRate.ToString(CultureInfo.InvariantCulture),
            "cartesia_version=" + CartesiaConstants.RealtimeApiVersion,
            "turn_end_timeout_ms=" + turnEndTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture));

        var builder = new UriBuilder(baseUri)
        {
            Scheme = scheme,
            Path = baseUri.AbsolutePath.TrimEnd('/') + "/stt/turns/websocket",
            Query = query,
        };

        return builder.Uri;
    }

    /// <summary>
    /// The model a session transcribes with: its own, or else the deployment's, or else the realtime default.
    /// </summary>
    /// <param name="options">The session's options.</param>
    internal string GetModel(RealtimeSessionOptions options)
    {
        var model = options?.TranscriptionOptions?.ModelId;

        if (string.IsNullOrWhiteSpace(model))
        {
            model = _model;
        }

        // ink-whisper transcribes whole recordings, and the turns endpoint does not take it.
        return string.IsNullOrWhiteSpace(model) || model.Trim().StartsWith("ink-whisper", StringComparison.OrdinalIgnoreCase)
            ? CartesiaConstants.DefaultRealtimeSpeechToTextModel
            : model.Trim();
    }

    /// <summary>
    /// How long Cartesia may wait after speech before it ends a turn: the silence the session's turn detection names,
    /// within the range Cartesia accepts, or else <see cref="DefaultTurnEndTimeoutMilliseconds"/>.
    /// </summary>
    /// <remarks>
    /// Turn-detection details ride the session's raw representation, since the realtime options cannot express them
    /// (see <see cref="RealtimeTurnDetectionOverrides"/>). Semantic detection names no silence.
    /// </remarks>
    /// <param name="options">The session's options.</param>
    internal static int GetTurnEndTimeout(RealtimeSessionOptions options)
    {
        if (options?.RawRepresentationFactory?.Invoke() is RealtimeTurnDetectionOverrides overrides &&
            string.Equals(overrides.Type, RealtimeTurnDetectionTypes.ServerVad, StringComparison.OrdinalIgnoreCase) &&
            overrides.SilenceDurationMs is > 0 and var silence)
        {
            return Math.Clamp(silence, MinimumTurnEndTimeoutMilliseconds, MaximumTurnEndTimeoutMilliseconds);
        }

        return DefaultTurnEndTimeoutMilliseconds;
    }

    /// <summary>
    /// What Cartesia refusing the connection with <paramref name="status"/> means, or <see langword="null"/> when it did
    /// not answer at all.
    /// </summary>
    /// <param name="status">The HTTP status of the refused connection.</param>
    internal static string RefusalMessage(HttpStatusCode status)
        => status switch
        {
            0 => null,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"Cartesia refused the API key (HTTP {(int)status}). Check the API key of the Cartesia deployment.",
            HttpStatusCode.BadRequest =>
                "Cartesia refused the session's settings (HTTP 400): its realtime speech-to-text does not offer the model or audio format asked for. Check the deployment's model (ink-2 or ink-preview).",
            HttpStatusCode.PaymentRequired =>
                "Cartesia refused the session (HTTP 402): the account has no credit left.",
            HttpStatusCode.TooManyRequests =>
                "Cartesia refused the session (HTTP 429): the account has too many requests or connections open.",
            _ => $"Cartesia refused the connection (HTTP {(int)status}).",
        };

    private static string GetEncoding(string mediaType)
        => mediaType?.Trim().ToLowerInvariant() switch
        {
            "audio/pcmu" => "pcm_mulaw",
            "audio/pcma" => "pcm_alaw",
            _ => "pcm_s16le",
        };
}
