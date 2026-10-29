using System.Net.Http.Headers;
using System.Text.Json;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;

namespace CrestApps.Core.AI.ElevenLabs.Services;

/// <summary>
/// The voices of an ElevenLabs account: search, and instant voice cloning from recordings.
/// </summary>
public sealed class ElevenLabsVoiceLibrary : IVoiceLibrary
{
    private readonly ElevenLabsApi _api;

    internal ElevenLabsVoiceLibrary(ElevenLabsApi api)
    {
        _api = api;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ElevenLabsVoiceLibrary"/> class.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="endpoint">The API base address, or <see langword="null"/> for the default.</param>
    /// <param name="apiKey">The API key.</param>
    public ElevenLabsVoiceLibrary(HttpClient http, Uri endpoint, string apiKey)
        : this(new ElevenLabsApi(http, endpoint, apiKey))
    {
    }

    /// <inheritdoc />
    public bool SupportsCloning => true;

    /// <inheritdoc />
    public async Task<VoiceSearchResult> SearchAsync(VoiceSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new List<string> { $"page_size={Math.Clamp(request.PageSize, 1, 100)}" };

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            query.Add($"search={Uri.EscapeDataString(request.Query.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(request.Cursor))
        {
            query.Add($"next_page_token={Uri.EscapeDataString(request.Cursor)}");
        }

        using var http = _api.CreateRequest(HttpMethod.Get, $"v2/voices?{string.Join('&', query)}");
        using var json = await _api.GetJsonAsync(http, cancellationToken);

        var voices = new List<SpeechVoice>();

        if (json.RootElement.TryGetProperty("voices", out var list))
        {
            foreach (var node in list.EnumerateArray())
            {
                var voice = ToVoice(node);

                // The API has no language or gender filter on this endpoint, so they are applied to the page.
                if (Matches(voice, node, request))
                {
                    voices.Add(voice);
                }
            }
        }

        var hasMore = json.RootElement.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True;
        var next = hasMore && json.RootElement.TryGetProperty("next_page_token", out var token) ? token.GetString() : null;

        return new VoiceSearchResult { Voices = voices, NextCursor = next };
    }

    /// <inheritdoc />
    public async Task<SpeechVoice> CloneAsync(VoiceCloneRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        if (request.Samples is not { Count: > 0 })
        {
            throw new ArgumentException("Cloning a voice needs at least one recording.", nameof(request));
        }

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(request.Name.Trim()), "name");

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            form.Add(new StringContent(request.Description.Trim()), "description");
        }

        var index = 0;

        foreach (var sample in request.Samples)
        {
            var file = new ByteArrayContent(sample.Content ?? throw new ArgumentException("A recording has no audio.", nameof(request)));
            file.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(sample.ContentType) ? "audio/mpeg" : sample.ContentType);
            form.Add(file, "files", string.IsNullOrWhiteSpace(sample.FileName) ? $"sample{++index}.mp3" : sample.FileName);
        }

        using var http = _api.CreateRequest(HttpMethod.Post, "v1/voices/add", form);
        using var json = await _api.GetJsonAsync(http, cancellationToken);

        var id = json.RootElement.TryGetProperty("voice_id", out var node) ? node.GetString() : null;

        if (string.IsNullOrEmpty(id))
        {
            throw new InvalidOperationException("ElevenLabs created the voice but did not return its id.");
        }

        return new SpeechVoice { Id = id, Name = request.Name.Trim(), Language = request.Language };
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string voiceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceId);

        using var http = _api.CreateRequest(HttpMethod.Delete, $"v1/voices/{Uri.EscapeDataString(voiceId)}");
        using var response = await _api.SendAsync(http, streaming: false, cancellationToken);
    }

    private static SpeechVoice ToVoice(JsonElement node)
    {
        var labels = node.TryGetProperty("labels", out var l) && l.ValueKind == JsonValueKind.Object ? l : default;

        return new SpeechVoice
        {
            Id = String(node, "voice_id"),
            Name = String(node, "name"),
            VoiceSampleUrl = String(node, "preview_url"),
            Language = FirstLanguage(node) ?? (labels.ValueKind == JsonValueKind.Object ? String(labels, "language") : null),
            Gender = ParseGender(labels.ValueKind == JsonValueKind.Object ? String(labels, "gender") : null),
        };
    }

    private static bool Matches(SpeechVoice voice, JsonElement node, VoiceSearchRequest request)
    {
        if (request.Gender is { } gender && voice.Gender != gender)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Language))
        {
            return true;
        }

        var wanted = Primary(request.Language);

        if (node.TryGetProperty("verified_languages", out var verified) && verified.ValueKind == JsonValueKind.Array && verified.GetArrayLength() > 0)
        {
            return verified.EnumerateArray().Any(item => string.Equals(Primary(String(item, "language")), wanted, StringComparison.OrdinalIgnoreCase));
        }

        // A voice that lists no languages is taken to speak the one it was made in, or to be multilingual.
        return voice.Language is null || string.Equals(Primary(voice.Language), wanted, StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstLanguage(JsonElement node)
    {
        return node.TryGetProperty("verified_languages", out var verified) && verified.ValueKind == JsonValueKind.Array && verified.GetArrayLength() > 0
            ? String(verified[0], "locale") ?? String(verified[0], "language")
            : null;
    }

    private static SpeechVoiceGender ParseGender(string value) => value?.ToLowerInvariant() switch
    {
        "male" => SpeechVoiceGender.Male,
        "female" => SpeechVoiceGender.Female,
        "neutral" => SpeechVoiceGender.Neutral,
        _ => SpeechVoiceGender.Unknown,
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
