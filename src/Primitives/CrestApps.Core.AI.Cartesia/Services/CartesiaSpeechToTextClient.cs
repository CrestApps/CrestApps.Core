#pragma warning disable MEAI001 // Speech-to-text APIs from Microsoft.Extensions.AI are preview and require explicit opt-in at each usage site.
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Cartesia.Services;

/// <summary>
/// An <see cref="ISpeechToTextClient"/> over the Cartesia Ink speech-to-text API.
/// </summary>
/// <remarks>
/// The API transcribes a whole recording, so streaming yields the finished transcript as one update. Audio without a
/// container is taken for raw 16-bit mono PCM at <see cref="SpeechToTextOptions.SpeechSampleRate"/> (16 kHz when
/// unset) and wrapped in a WAV header. The API has no vocabulary hints, so boosted keywords are ignored.
/// </remarks>
public sealed class CartesiaSpeechToTextClient : ISpeechToTextClient
{
    private readonly CartesiaApi _api;
    private readonly string _defaultModel;

    internal CartesiaSpeechToTextClient(CartesiaApi api, string defaultModel)
    {
        _api = api;
        _defaultModel = string.IsNullOrWhiteSpace(defaultModel) ? CartesiaConstants.DefaultSpeechToTextModel : defaultModel;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CartesiaSpeechToTextClient"/> class.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="endpoint">The API base address, or <see langword="null"/> for the default.</param>
    /// <param name="apiKey">The API key.</param>
    /// <param name="defaultModel">The model used when a request names none.</param>
    public CartesiaSpeechToTextClient(HttpClient http, Uri endpoint, string apiKey, string defaultModel = null)
        : this(new CartesiaApi(http, endpoint, apiKey), defaultModel)
    {
    }

    /// <inheritdoc />
    public async Task<SpeechToTextResponse> GetTextAsync(Stream audioSpeechStream, SpeechToTextOptions options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioSpeechStream);

        using var buffer = new MemoryStream();
        await audioSpeechStream.CopyToAsync(buffer, cancellationToken);

        var audio = SpeechAudioFormat.ToUploadable(buffer.ToArray(), options?.SpeechSampleRate);
        var model = string.IsNullOrWhiteSpace(options?.ModelId) ? _defaultModel : options.ModelId;

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(model), "model");

        if (LanguageCode(options?.SpeechLanguage) is { } language)
        {
            form.Add(new StringContent(language), "language");
        }

        var file = new ByteArrayContent(audio.Content);
        file.Headers.ContentType = new MediaTypeHeaderValue(audio.ContentType);
        form.Add(file, "file", audio.FileName);

        using var request = _api.CreateRequest(HttpMethod.Post, "stt", form);
        using var json = await _api.GetJsonAsync(request, cancellationToken);

        var text = json.RootElement.TryGetProperty("text", out var textNode) ? textNode.GetString() : null;

        return new SpeechToTextResponse(text?.Trim() ?? string.Empty)
        {
            ModelId = model,
            RawRepresentation = json.RootElement.Clone(),
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetTextAsync(audioSpeechStream, options, cancellationToken);

        yield return new SpeechToTextResponseUpdate(response.Text)
        {
            Kind = SpeechToTextResponseUpdateKind.TextUpdated,
            ModelId = response.ModelId,
        };
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
