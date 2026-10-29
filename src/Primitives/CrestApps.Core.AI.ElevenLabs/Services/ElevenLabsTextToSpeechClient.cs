#pragma warning disable MEAI001 // Text-to-speech APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.ElevenLabs.Services;

/// <summary>
/// An <see cref="ITextToSpeechClient"/> over the ElevenLabs text-to-speech API.
/// </summary>
/// <remarks>
/// A realtime session asks for raw PCM by naming the format <c>Pcm</c> and a sample rate (<c>Pcm24000</c>); any
/// other request gets MP3. Speed is limited to the 0.7 to 1.2 range the API accepts. Neutral speech markup
/// (<see cref="SpeechMarkup"/>) is rendered as ElevenLabs break tags.
/// </remarks>
public sealed class ElevenLabsTextToSpeechClient : ITextToSpeechClient
{
    internal const float MinimumSpeed = 0.7f;

    internal const float MaximumSpeed = 1.2f;

    private static readonly int[] _pcmRates = [8000, 16000, 22050, 24000, 32000, 44100, 48000];

    private readonly ElevenLabsApi _api;
    private readonly string _defaultModel;

    internal ElevenLabsTextToSpeechClient(ElevenLabsApi api, string defaultModel)
    {
        _api = api;
        _defaultModel = string.IsNullOrWhiteSpace(defaultModel) ? ElevenLabsConstants.DefaultTextToSpeechModel : defaultModel;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ElevenLabsTextToSpeechClient"/> class.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="endpoint">The API base address, or <see langword="null"/> for the default.</param>
    /// <param name="apiKey">The API key.</param>
    /// <param name="defaultModel">The model used when a request names none.</param>
    public ElevenLabsTextToSpeechClient(HttpClient http, Uri endpoint, string apiKey, string defaultModel = null)
        : this(new ElevenLabsApi(http, endpoint, apiKey), defaultModel)
    {
    }

    /// <inheritdoc />
    public async Task<TextToSpeechResponse> GetAudioAsync(string text, TextToSpeechOptions options = null, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(text, options, stream: false, out var mediaType);
        using var response = await _api.SendAsync(request, streaming: false, cancellationToken);
        var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        return new TextToSpeechResponse([new DataContent(audio, mediaType)]) { ModelId = options?.ModelId ?? _defaultModel };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(text, options, stream: true, out var mediaType);
        using var response = await _api.SendAsync(request, streaming: true, cancellationToken);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);

        var raw = mediaType == SpeechAudioFormat.PcmMediaType;
        var buffer = new byte[8192];

        // A 16-bit sample can straddle two reads. Playing half a sample as the start of the next chunk shifts every
        // sample after it by a byte, which is heard as loud static, so an odd trailing byte waits for the next read.
        byte? carry = null;
        int read;

        while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var chunk = new byte[read + (carry.HasValue ? 1 : 0)];
            var offset = 0;

            if (carry.HasValue)
            {
                chunk[0] = carry.Value;
                offset = 1;
                carry = null;
            }

            Array.Copy(buffer, 0, chunk, offset, read);

            if (raw && chunk.Length % 2 == 1)
            {
                carry = chunk[^1];
                Array.Resize(ref chunk, chunk.Length - 1);
            }

            if (chunk.Length > 0)
            {
                yield return new TextToSpeechResponseUpdate([new DataContent(chunk, mediaType)]) { ModelId = options?.ModelId ?? _defaultModel };
            }
        }
    }

    /// <inheritdoc />
    public object GetService(Type serviceType, object serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private HttpRequestMessage CreateRequest(string text, TextToSpeechOptions options, bool stream, out string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var voice = options?.VoiceId;

        if (string.IsNullOrWhiteSpace(voice))
        {
            throw new InvalidOperationException("ElevenLabs needs a voice id. Choose a voice for the deployment or the profile.");
        }

        var format = ResolveOutputFormat(options?.AudioFormat, out mediaType);
        var model = string.IsNullOrWhiteSpace(options?.ModelId) ? _defaultModel : options.ModelId;
        var body = new Dictionary<string, object>
        {
            ["text"] = SpeechMarkup.Render(text, SpeechMarkupDialect.ElevenLabs),
            ["model_id"] = model,
        };

        // The multilingual v2 models detect the language themselves and refuse a language code.
        if (LanguageCode(options?.Language) is { } language && !model.Contains("multilingual_v2", StringComparison.OrdinalIgnoreCase))
        {
            body["language_code"] = language;
        }

        if (options?.Speed is float speed)
        {
            body["voice_settings"] = new { speed = Math.Clamp(speed, MinimumSpeed, MaximumSpeed) };
        }

        var path = $"v1/text-to-speech/{Uri.EscapeDataString(voice)}{(stream ? "/stream" : string.Empty)}?output_format={format}";

        return _api.CreateRequest(HttpMethod.Post, path, ElevenLabsApi.Json(body));
    }

    private static string ResolveOutputFormat(string audioFormat, out string mediaType)
    {
        if (SpeechAudioFormat.TryGetPcmSampleRate(audioFormat, out var rate))
        {
            if (!_pcmRates.Contains(rate))
            {
                throw new NotSupportedException($"ElevenLabs cannot produce raw PCM at {rate} Hz. Supported rates: {string.Join(", ", _pcmRates)}.");
            }

            mediaType = SpeechAudioFormat.PcmMediaType;

            return $"pcm_{rate}";
        }

        mediaType = "audio/mpeg";

        return "mp3_44100_128";
    }

    // ElevenLabs takes an ISO 639-1 code: "es-MX" is "es".
    private static string LanguageCode(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var separator = language.IndexOf('-');

        return (separator > 0 ? language[..separator] : language).ToLowerInvariant();
    }
}
