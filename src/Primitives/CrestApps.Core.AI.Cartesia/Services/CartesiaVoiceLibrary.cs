using System.Net.Http.Headers;
using System.Text.Json;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;

namespace CrestApps.Core.AI.Cartesia.Services;

/// <summary>
/// The voices of a Cartesia account: search, and cloning from a recording.
/// </summary>
/// <remarks>
/// Cartesia clones from one clip (<see cref="VoiceCloneRequest.Samples"/>'s first recording is used) and asks for the
/// language it is in, which defaults to English.
/// </remarks>
public sealed class CartesiaVoiceLibrary : IVoiceLibrary
{
    private readonly CartesiaApi _api;

    internal CartesiaVoiceLibrary(CartesiaApi api)
    {
        _api = api;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CartesiaVoiceLibrary"/> class.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="endpoint">The API base address, or <see langword="null"/> for the default.</param>
    /// <param name="apiKey">The API key.</param>
    public CartesiaVoiceLibrary(HttpClient http, Uri endpoint, string apiKey)
        : this(new CartesiaApi(http, endpoint, apiKey))
    {
    }

    /// <inheritdoc />
    public bool SupportsCloning => true;

    /// <inheritdoc />
    public async Task<VoiceSearchResult> SearchAsync(VoiceSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new List<string> { $"limit={Math.Clamp(request.PageSize, 1, 100)}" };

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            query.Add($"q={Uri.EscapeDataString(request.Query.Trim())}");
        }

        if (request.Gender is { } gender && ToApiGender(gender) is { } apiGender)
        {
            query.Add($"gender={apiGender}");
        }

        if (!string.IsNullOrWhiteSpace(request.Cursor))
        {
            query.Add($"starting_after={Uri.EscapeDataString(request.Cursor)}");
        }

        using var http = _api.CreateRequest(HttpMethod.Get, $"voices?{string.Join('&', query)}");
        using var json = await _api.GetJsonAsync(http, cancellationToken);

        var voices = new List<SpeechVoice>();

        if (json.RootElement.TryGetProperty("data", out var list))
        {
            foreach (var node in list.EnumerateArray())
            {
                var voice = ToVoice(node);

                // The API filters by gender but not by language, so language is applied to the page.
                if (string.IsNullOrWhiteSpace(request.Language) ||
                    string.Equals(Primary(voice.Language), Primary(request.Language), StringComparison.OrdinalIgnoreCase))
                {
                    voices.Add(voice);
                }
            }
        }

        var hasMore = json.RootElement.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True;
        var next = hasMore && json.RootElement.TryGetProperty("next_page", out var token) && token.ValueKind == JsonValueKind.String
            ? token.GetString()
            : null;

        return new VoiceSearchResult { Voices = voices, NextCursor = next };
    }

    /// <inheritdoc />
    public async Task<SpeechVoice> CloneAsync(VoiceCloneRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        if (request.Samples is not { Count: > 0 })
        {
            throw new ArgumentException("Cloning a voice needs a recording.", nameof(request));
        }

        var sample = request.Samples[0];
        var language = Primary(request.Language)?.ToLowerInvariant() ?? "en";

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(request.Name.Trim()), "name");
        form.Add(new StringContent(language), "language");
        form.Add(new StringContent("similarity"), "mode");

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            form.Add(new StringContent(request.Description.Trim()), "description");
        }

        var clip = new ByteArrayContent(sample.Content ?? throw new ArgumentException("The recording has no audio.", nameof(request)));
        clip.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(sample.ContentType) ? "audio/wav" : sample.ContentType);
        form.Add(clip, "clip", string.IsNullOrWhiteSpace(sample.FileName) ? "clip.wav" : sample.FileName);

        using var http = _api.CreateRequest(HttpMethod.Post, "voices/clone", form);
        using var json = await _api.GetJsonAsync(http, cancellationToken);

        var voice = ToVoice(json.RootElement);

        if (string.IsNullOrEmpty(voice.Id))
        {
            throw new InvalidOperationException("Cartesia created the voice but did not return its id.");
        }

        voice.Name ??= request.Name.Trim();
        voice.Language ??= language;

        return voice;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string voiceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceId);

        using var http = _api.CreateRequest(HttpMethod.Delete, $"voices/{Uri.EscapeDataString(voiceId)}");
        using var response = await _api.SendAsync(http, streaming: false, cancellationToken);
    }

    private static SpeechVoice ToVoice(JsonElement node) => new()
    {
        Id = String(node, "id"),
        Name = String(node, "name"),
        Language = String(node, "language"),
        Gender = String(node, "gender")?.ToLowerInvariant() switch
        {
            "masculine" => SpeechVoiceGender.Male,
            "feminine" => SpeechVoiceGender.Female,
            "gender_neutral" => SpeechVoiceGender.Neutral,
            _ => SpeechVoiceGender.Unknown,
        },
    };

    private static string ToApiGender(SpeechVoiceGender gender) => gender switch
    {
        SpeechVoiceGender.Male => "masculine",
        SpeechVoiceGender.Female => "feminine",
        SpeechVoiceGender.Neutral => "gender_neutral",
        _ => null,
    };

    private static string Primary(string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return language;
        }

        var separator = language.IndexOfAny(['-', '_']);

        return separator > 0 ? language[..separator] : language;
    }

    private static string String(JsonElement node, string name)
        => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
