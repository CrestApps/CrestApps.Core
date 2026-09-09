using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Azure.Core;
using Azure.Identity;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Azure.Models;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

#pragma warning disable MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
namespace CrestApps.Core.AI.OpenAI.Azure.Services;

/// <summary>
/// An <see cref="ITextToSpeechClient"/> implementation that uses the Azure Speech SDK
/// for text-to-speech synthesis. Supports streaming audio output via <see cref="SpeechSynthesizer"/>.
/// </summary>
public sealed class AzureSpeechServiceTextToSpeechClient : ITextToSpeechClient
{
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";
    private const string DefaultContentType = "audio/mp3";

    // Raw PCM carries no container to describe itself, so callers that decode it — the realtime pipeline,
    // for one — rely on this media type to know what the bytes are.
    private const string PcmContentType = "audio/L16";

    private static readonly SpeechSynthesisOutputFormat _defaultOutputFormat = SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3;

    // The formats a caller can ask for by name. Raw PCM is what an audio pipeline wants; MP3 is what a
    // browser plays directly, and stays the default.
    private static readonly Dictionary<string, SpeechSynthesisOutputFormat> _outputFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pcm8000"] = SpeechSynthesisOutputFormat.Raw8Khz16BitMonoPcm,
        ["pcm16000"] = SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm,
        ["pcm24000"] = SpeechSynthesisOutputFormat.Raw24Khz16BitMonoPcm,
        ["pcm48000"] = SpeechSynthesisOutputFormat.Raw48Khz16BitMonoPcm,
        ["riff8000"] = SpeechSynthesisOutputFormat.Riff8Khz16BitMonoPcm,
        ["riff16000"] = SpeechSynthesisOutputFormat.Riff16Khz16BitMonoPcm,
        ["riff24000"] = SpeechSynthesisOutputFormat.Riff24Khz16BitMonoPcm,
        ["riff48000"] = SpeechSynthesisOutputFormat.Riff48Khz16BitMonoPcm,
        ["mp3"] = SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3,
        ["mp3_16000"] = SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3,
        ["mp3_24000"] = SpeechSynthesisOutputFormat.Audio24Khz48KBitRateMonoMp3,
        ["mp3_48000"] = SpeechSynthesisOutputFormat.Audio48Khz96KBitRateMonoMp3,
    };

    private static readonly string[] _regionSuffixes =
    [
        ".api.cognitive.microsoft.com",
        ".tts.speech.microsoft.com",
        ".stt.speech.microsoft.com",
    ];

    private readonly Uri _endpoint;
    private readonly AzureAuthenticationType _authType;
    private readonly string _apiKey;
    private readonly string _identityId;
    private readonly string _region;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AzureSpeechServiceTextToSpeechClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string _cachedToken;
    private DateTimeOffset _tokenExpires;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureSpeechServiceTextToSpeechClient"/> class.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="authType">The auth type.</param>
    /// <param name="apiKey">The api key.</param>
    /// <param name="identityId">The identity id.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public AzureSpeechServiceTextToSpeechClient(
        Uri endpoint,
        AzureAuthenticationType authType,
        string apiKey,
        string identityId,
        TimeProvider timeProvider,
        ILogger<AzureSpeechServiceTextToSpeechClient> logger)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        _endpoint = endpoint;
        _authType = authType;
        _apiKey = apiKey;
        _identityId = identityId;
        _region = TryExtractRegion(endpoint);
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets audio.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<TextToSpeechResponse> GetAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Starting single-shot speech synthesis. VoiceId: {VoiceId}, Endpoint: {Endpoint}, AuthType: {AuthType}",
                options?.VoiceId ?? "(default)", _endpoint, _authType);
        }

        var speechConfig = await CreateSpeechConfigAsync(options, cancellationToken);

        // Use null audio config to get in-memory audio result.
        using var synthesizer = new SpeechSynthesizer(speechConfig, null);
        var result = await synthesizer.SpeakTextAsync(text);

        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Single-shot synthesis succeeded. Audio bytes: {AudioBytes}", result.AudioData.Length);
            }

            return new TextToSpeechResponse(new List<AIContent>
            {
                new DataContent(result.AudioData, ResolveContentType(options?.AudioFormat)),
            });
        }

        if (result.Reason == ResultReason.Canceled)
        {
            var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
            _logger.LogWarning(
                "Azure Speech SDK synthesis canceled: Reason={Reason}, ErrorCode={ErrorCode}, ErrorDetails={ErrorDetails}",
                cancellation.Reason, cancellation.ErrorCode, cancellation.ErrorDetails);

            throw new InvalidOperationException(
                $"Speech synthesis canceled: {cancellation.ErrorCode} - {cancellation.ErrorDetails}");
        }

        return null;
    }

    /// <summary>
    /// Gets streaming audio.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Starting streaming speech synthesis. VoiceId: {VoiceId}, Endpoint: {Endpoint}, AuthType: {AuthType}",
                options?.VoiceId ?? "(default)", _endpoint, _authType);
        }

        var speechConfig = await CreateSpeechConfigAsync(options, cancellationToken);
        var contentType = ResolveContentType(options?.AudioFormat);

        // Use null audio config to get in-memory audio.
        using var synthesizer = new SpeechSynthesizer(speechConfig, null);

        var channel = Channel.CreateUnbounded<TextToSpeechResponseUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        synthesizer.Synthesizing += (_, e) =>
        {
            if (e.Result.AudioData?.Length > 0)
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Streaming synthesis chunk: {AudioBytes} bytes", e.Result.AudioData.Length);
                }

                channel.Writer.TryWrite(new TextToSpeechResponseUpdate(new List<AIContent>
                {
                    new DataContent(e.Result.AudioData, contentType),
                })
                {
                    Kind = TextToSpeechResponseUpdateKind.AudioUpdating,
                });
            }
        };

        synthesizer.SynthesisCompleted += (_, e) =>
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Streaming synthesis completed. Total audio length: {AudioDuration}", e.Result.AudioDuration);
            }

            channel.Writer.TryComplete();
        };

        synthesizer.SynthesisCanceled += (_, e) =>
        {
            var cancellation = SpeechSynthesisCancellationDetails.FromResult(e.Result);

            if (cancellation.Reason == CancellationReason.Error)
            {
                _logger.LogWarning(
                    "Azure Speech SDK streaming synthesis canceled with error. ErrorCode={ErrorCode}, ErrorDetails={ErrorDetails}",
                    cancellation.ErrorCode, cancellation.ErrorDetails);

                channel.Writer.TryComplete(new InvalidOperationException(cancellation.ErrorDetails));
            }
            else
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("Azure Speech SDK streaming synthesis ended with reason: {Reason}", cancellation.Reason);
                }

                channel.Writer.TryComplete();
            }
        };

        // Start synthesis - this returns once synthesis begins, not when it finishes.
        var speakTask = synthesizer.StartSpeakingTextAsync(text);

        await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return update;
        }

        // Await the task to propagate any startup exceptions.
        await speakTask;

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Streaming synthesis iteration completed.");
        }
    }

    /// <summary>
    /// Gets the available voices for text-to-speech synthesis.
    /// </summary>
    /// <param name="locale">An optional locale to filter voices (e.g., "en-US"). If <c>null</c>, all voices are returned.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An array of available <see cref="SpeechVoice"/> instances.</returns>
    public async Task<SpeechVoice[]> GetVoicesAsync(
        string locale = null,
        CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Fetching available voices. Locale: {Locale}, Endpoint: {Endpoint}", locale ?? "(all)", _endpoint);
        }

        var speechConfig = await CreateSpeechConfigAsync(null, cancellationToken);

        using var synthesizer = new SpeechSynthesizer(speechConfig, null);
        var result = await synthesizer.GetVoicesAsync(locale ?? string.Empty);

        if (result.Reason == ResultReason.VoicesListRetrieved)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Retrieved {VoiceCount} voices.", result.Voices.Count);
            }

            return result.Voices
                .Select(v => new SpeechVoice
                {
                    Id = v.ShortName,
                    Name = v.LocalName,
                    Language = v.Locale,
                    Gender = MapGender(v.Gender),
                })
            .ToArray();
        }

        _logger.LogWarning("Failed to retrieve voices. Reason: {Reason}", result.Reason);

        return [];
    }

    /// <summary>
    /// Gets service.
    /// </summary>
    /// <param name="serviceType">The service type.</param>
    /// <param name="serviceKey">The service key.</param>
    public object GetService(Type serviceType, object serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <summary>
    /// Disposes the operation.
    /// </summary>
    public void Dispose()
    {
        // No owned resources to dispose; SpeechConfig/synthesizers are disposed per-call.
    }

    private async Task<SpeechConfig> CreateSpeechConfigAsync(TextToSpeechOptions options, CancellationToken cancellationToken)
    {
        SpeechConfig config;

        if (_authType == AzureAuthenticationType.ApiKey)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Using endpoint-based SDK configuration for API key authentication. Endpoint: {Endpoint}, Region: {Region}",
                    _endpoint,
                    _region ?? "(unknown)");
            }

            config = await CreateEndpointBasedConfigAsync(cancellationToken);
        }
        else if (_region != null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Using region '{Region}' for SDK configuration.", _region);
            }

            config = await CreateRegionBasedConfigAsync(_region, cancellationToken);
        }
        else
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Could not extract region from endpoint '{Endpoint}'. Falling back to endpoint-based SDK configuration.",
                    _endpoint);
            }

            config = await CreateEndpointBasedConfigAsync(cancellationToken);
        }

        if (!string.IsNullOrEmpty(options?.VoiceId))
        {
            config.SpeechSynthesisVoiceName = options.VoiceId;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Using voice: {VoiceId}", options.VoiceId);
            }
        }

        if (!string.IsNullOrEmpty(options?.Language))
        {
            config.SpeechSynthesisLanguage = options.Language;
        }

        // MP3 stays the default, for browser compatibility. A caller that needs raw PCM — an audio
        // pipeline that mixes or resamples the result rather than handing it to an <audio> element — asks
        // for it by name through the options.
        config.SetSpeechSynthesisOutputFormat(ResolveOutputFormat(options?.AudioFormat));

        return config;
    }

    /// <summary>
    /// Resolves the synthesis output format a caller asked for by name, falling back to the MP3 default
    /// when the name is missing or is not one this client offers.
    /// </summary>
    /// <param name="audioFormat">The requested format name, such as <c>Pcm24000</c>.</param>
    private static SpeechSynthesisOutputFormat ResolveOutputFormat(string audioFormat)
    {
        if (string.IsNullOrWhiteSpace(audioFormat))
        {
            return _defaultOutputFormat;
        }

        // Names arrive in whatever shape the caller uses - "Pcm24000", "pcm_24000", "PCM-24000" - so they
        // are compared with the separators removed.
        var normalized = Normalize(audioFormat);

        if (_outputFormats.TryGetValue(normalized, out var outputFormat))
        {
            return outputFormat;
        }

        // The Speech SDK's own enum names are accepted too, so a caller that knows this is Azure can ask
        // for a format this client does not list.
        if (Enum.TryParse<SpeechSynthesisOutputFormat>(audioFormat, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return _defaultOutputFormat;
    }

    /// <summary>
    /// Gets the media type describing the audio the requested format produces.
    /// </summary>
    /// <param name="audioFormat">The requested format name, such as <c>Pcm24000</c>.</param>
    private static string ResolveContentType(string audioFormat)
    {
        return ToContentType(ResolveOutputFormat(audioFormat));
    }

    /// <summary>
    /// Gets the media type describing the audio a synthesis output format produces.
    /// </summary>
    /// <param name="outputFormat">The synthesis output format.</param>
    private static string ToContentType(SpeechSynthesisOutputFormat outputFormat)
    {
        var name = outputFormat.ToString();

        if (name.EndsWith("MonoPcm", StringComparison.Ordinal))
        {
            // A RIFF format is PCM wrapped in a WAV container, which callers have to be told apart from the
            // raw stream because the header is not audio.
            return name.StartsWith("Riff", StringComparison.Ordinal)
                ? "audio/wav"
                : PcmContentType;
        }

        if (name.Contains("Mp3", StringComparison.Ordinal))
        {
            return DefaultContentType;
        }

        if (name.Contains("Opus", StringComparison.Ordinal))
        {
            return "audio/opus";
        }

        if (name.Contains("Webm", StringComparison.Ordinal))
        {
            return "audio/webm";
        }

        if (name.Contains("MULaw", StringComparison.Ordinal))
        {
            return "audio/basic";
        }

        return DefaultContentType;
    }

    /// <summary>
    /// Removes the separators a format name may be written with so names compare regardless of style.
    /// </summary>
    /// <param name="value">The format name.</param>
    private static string Normalize(string value)
    {
        return new string([.. value.Where(char.IsLetterOrDigit)]);
    }

    private async Task<SpeechConfig> CreateRegionBasedConfigAsync(string region, CancellationToken cancellationToken)
    {
        return _authType switch
        {
            AzureAuthenticationType.ApiKey
                => SpeechConfig.FromSubscription(_apiKey, region),
            AzureAuthenticationType.ManagedIdentity or AzureAuthenticationType.Default
                => SpeechConfig.FromAuthorizationToken(
                    await GetAuthorizationTokenAsync(cancellationToken), region),
            _ => throw new NotSupportedException(
                $"Authentication type '{_authType}' is not supported for Azure Speech."),
        };
    }

    private async Task<SpeechConfig> CreateEndpointBasedConfigAsync(CancellationToken cancellationToken)
    {
        return _authType switch
        {
            AzureAuthenticationType.ApiKey
                => SpeechConfig.FromEndpoint(_endpoint, _apiKey),
            AzureAuthenticationType.ManagedIdentity or AzureAuthenticationType.Default
                => CreateEndpointConfigWithToken(
                    await GetAuthorizationTokenAsync(cancellationToken)),
            _ => throw new NotSupportedException(
                $"Authentication type '{_authType}' is not supported for Azure Speech."),
        };
    }

    private SpeechConfig CreateEndpointConfigWithToken(string token)
    {
        var config = SpeechConfig.FromEndpoint(_endpoint);
        config.AuthorizationToken = token;

        return config;
    }

    private async Task<string> GetAuthorizationTokenAsync(CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken != null && _tokenExpires > _timeProvider.GetUtcNow().AddMinutes(-1))
            {
                return _cachedToken;
            }

            TokenCredential credential = _authType switch
            {
                AzureAuthenticationType.ManagedIdentity => string.IsNullOrEmpty(_identityId)
                ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
                : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(_identityId)),
                _ => new DefaultAzureCredential(),
            };

            var tokenResult = await credential.GetTokenAsync(
                new TokenRequestContext([CognitiveServicesScope]),
            cancellationToken);

            _cachedToken = tokenResult.Token;
            _tokenExpires = tokenResult.ExpiresOn;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Successfully obtained authorization token for Azure Speech TTS. AuthType: {AuthType}", _authType);
            }

            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string TryExtractRegion(Uri endpoint)
    {
        var host = endpoint.Host;

        foreach (var suffix in _regionSuffixes)
        {
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var region = host[..^suffix.Length];

                if (!string.IsNullOrEmpty(region))
                {
                    return region;
                }
            }
        }

        return null;
    }

    private static SpeechVoiceGender MapGender(SynthesisVoiceGender gender)
    {
        return gender switch
        {
            SynthesisVoiceGender.Male => SpeechVoiceGender.Male,
            SynthesisVoiceGender.Female => SpeechVoiceGender.Female,
            SynthesisVoiceGender.Neutral => SpeechVoiceGender.Neutral,
            _ => SpeechVoiceGender.Unknown,
        };
    }
}
