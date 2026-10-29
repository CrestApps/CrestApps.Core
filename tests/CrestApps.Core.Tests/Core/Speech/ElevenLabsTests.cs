#pragma warning disable MEAI001 // Speech APIs from Microsoft.Extensions.AI are preview.
#nullable enable
using System.Net;
using System.Text.Json;
using CrestApps.Core.AI.ElevenLabs;
using CrestApps.Core.AI.ElevenLabs.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Speech;

public sealed class ElevenLabsTests
{
    private static readonly Uri _regional = new("https://api.eu.residency.elevenlabs.io");

    private static ElevenLabsApi Api(StubHttpHandler handler, Uri? endpoint = null)
        => new(new HttpClient(handler), endpoint, "secret-key");

    [Fact]
    public async Task Streaming_SendsTheVoiceModelLanguageAndSpeedWithTheKeyInTheHeader()
    {
        var handler = StubHttpHandler.Bytes([1, 2]);
        var client = new ElevenLabsTextToSpeechClient(Api(handler), null);

        await foreach (var _ in client.GetStreamingAudioAsync("Hello there", new TextToSpeechOptions
        {
            VoiceId = "voice 1",
            ModelId = "eleven_flash_v2_5",
            Language = "es-MX",
            Speed = 2f,
            AudioFormat = "Pcm24000",
        }, TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal("https://api.elevenlabs.io/v1/text-to-speech/voice%201/stream?output_format=pcm_24000", handler.Last.RequestUri!.AbsoluteUri);
        Assert.Equal("secret-key", handler.Last.Headers.GetValues("xi-api-key").Single());

        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("Hello there", body.RootElement.GetProperty("text").GetString());
        Assert.Equal("eleven_flash_v2_5", body.RootElement.GetProperty("model_id").GetString());
        Assert.Equal("es", body.RootElement.GetProperty("language_code").GetString());

        // The API accepts 0.7 to 1.2.
        Assert.Equal(1.2f, body.RootElement.GetProperty("voice_settings").GetProperty("speed").GetSingle());
    }

    [Fact]
    public async Task Streaming_OmitsTheLanguageForMultilingualV2_AndUsesTheRegionalEndpoint()
    {
        var handler = StubHttpHandler.Bytes([1, 2]);
        var client = new ElevenLabsTextToSpeechClient(Api(handler, _regional), null);

        await foreach (var _ in client.GetStreamingAudioAsync("Hola", new TextToSpeechOptions { VoiceId = "v", ModelId = "eleven_multilingual_v2", Language = "es" }, TestContext.Current.CancellationToken))
        {
        }

        Assert.StartsWith("https://api.eu.residency.elevenlabs.io/v1/", handler.Last.RequestUri!.AbsoluteUri);
        Assert.False(JsonDocument.Parse(handler.LastBody).RootElement.TryGetProperty("language_code", out _));
    }

    [Fact]
    public async Task Streaming_UsesTheDefaultModelAndMp3WhenNoneIsAsked()
    {
        var handler = StubHttpHandler.Bytes([1, 2]);
        var client = new ElevenLabsTextToSpeechClient(Api(handler), null);

        var updates = new List<TextToSpeechResponseUpdate>();

        await foreach (var update in client.GetStreamingAudioAsync("Hi", new TextToSpeechOptions { VoiceId = "v" }, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        Assert.EndsWith("output_format=mp3_44100_128", handler.Last.RequestUri!.AbsoluteUri);
        Assert.Equal(ElevenLabsConstants.DefaultTextToSpeechModel, JsonDocument.Parse(handler.LastBody).RootElement.GetProperty("model_id").GetString());
        Assert.Equal("audio/mpeg", Assert.IsType<DataContent>(updates[0].Contents[0]).MediaType);
    }

    [Fact]
    public async Task Streaming_NeverSplitsA16BitSampleAcrossChunks()
    {
        // A socket delivers 3 bytes, then 5, then 4: the odd bytes must be carried to the next chunk.
        var handler = StubHttpHandler.Bytes([1, 2, 3], [4, 5, 6, 7, 8], [9, 10, 11, 12]);
        var client = new ElevenLabsTextToSpeechClient(Api(handler), null);

        var chunks = new List<byte[]>();

        await foreach (var update in client.GetStreamingAudioAsync("Hi", new TextToSpeechOptions { VoiceId = "v", AudioFormat = "Pcm16000" }, TestContext.Current.CancellationToken))
        {
            var audio = Assert.IsType<DataContent>(update.Contents[0]);
            Assert.Equal("audio/pcm", audio.MediaType);
            chunks.Add(audio.Data.ToArray());
        }

        Assert.All(chunks, chunk => Assert.Equal(0, chunk.Length % 2));
        Assert.Equal(Enumerable.Range(1, 12).Select(i => (byte)i), chunks.SelectMany(c => c));
    }

    [Fact]
    public async Task GetAudio_ReturnsTheWholeRecording()
    {
        var handler = StubHttpHandler.Bytes([9, 8, 7]);
        var client = new ElevenLabsTextToSpeechClient(Api(handler), null);

        var response = await client.GetAudioAsync("Hi", new TextToSpeechOptions { VoiceId = "v" }, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("/stream", handler.Last.RequestUri!.AbsoluteUri);
        Assert.Equal([9, 8, 7], Assert.IsType<DataContent>(response.Contents[0]).Data.ToArray());
    }

    [Fact]
    public async Task NeutralMarkup_IsRenderedAsElevenLabsTags()
    {
        var handler = StubHttpHandler.Bytes([1, 2]);
        var client = new ElevenLabsTextToSpeechClient(Api(handler), null);

        await client.GetAudioAsync("Wait<break time=\"500ms\"/>then <spell>AB1</spell>", new TextToSpeechOptions { VoiceId = "v" }, TestContext.Current.CancellationToken);

        Assert.Equal("Wait<break time=\"0.5s\" />then A-B-1", JsonDocument.Parse(handler.LastBody).RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Refuses_ARequestWithoutAVoice_AndARateItCannotProduce()
    {
        var client = new ElevenLabsTextToSpeechClient(Api(StubHttpHandler.Bytes([1, 2])), null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAudioAsync("Hi", null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetAudioAsync("Hi", new TextToSpeechOptions { VoiceId = "v", AudioFormat = "Pcm12345" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnErrorAnswer_SaysWhatWentWrong()
    {
        var client = new ElevenLabsTextToSpeechClient(Api(new StubHttpHandler("""{"detail":"invalid_api_key"}""", HttpStatusCode.Unauthorized)), null);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAudioAsync("Hi", new TextToSpeechOptions { VoiceId = "v" }, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Contains("API key", error.Message);
        Assert.Contains("invalid_api_key", error.Message);
        Assert.DoesNotContain("secret-key", error.Message);
    }

    [Fact]
    public async Task SpeechToText_UploadsRawPcmAsWavWithTheLanguageAndReadsTheTranscript()
    {
        var handler = new StubHttpHandler("""{"text":" hello world ","language_code":"eng"}""");
        var client = new ElevenLabsSpeechToTextClient(Api(handler), null);

        var response = await client.GetTextAsync(
            new MemoryStream([1, 2, 3, 4]),
            new SpeechToTextOptions { SpeechLanguage = "en-US", SpeechSampleRate = 24000 },
            TestContext.Current.CancellationToken);

        Assert.Equal("hello world", response.Text);
        Assert.Equal("https://api.elevenlabs.io/v1/speech-to-text", handler.Last.RequestUri!.AbsoluteUri);
        Assert.Contains("name=model_id", handler.LastBody);
        Assert.Contains(ElevenLabsConstants.DefaultSpeechToTextModel, handler.LastBody);
        Assert.Contains("name=language_code", handler.LastBody);
        Assert.Contains("filename=audio.wav", handler.LastBody);
        Assert.Contains("RIFF", handler.LastBody);
        Assert.DoesNotContain("keyterms", handler.LastBody);
    }

    [Fact]
    public async Task SpeechToText_SendsBoostedKeywordsOnlyToModelsThatTakeThem()
    {
        var keywords = new SpeechToTextOptions { ModelId = "scribe_v2", AdditionalProperties = new() { [SpeechToTextOptionsProperties.BoostedKeywords] = new[] { "Zyxel", "Acme" } } };
        var handler = new StubHttpHandler("""{"text":"x"}""");
        var client = new ElevenLabsSpeechToTextClient(Api(handler), null);

        await client.GetTextAsync(new MemoryStream([1, 2]), keywords, TestContext.Current.CancellationToken);

        Assert.Contains("name=keyterms", handler.LastBody);
        Assert.Contains("Zyxel", handler.LastBody);

        keywords.ModelId = "scribe_v1";
        await client.GetTextAsync(new MemoryStream([1, 2]), keywords, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("keyterms", handler.LastBody);
    }

    [Fact]
    public async Task SpeechToText_StreamingYieldsTheFinishedTranscript()
    {
        var client = new ElevenLabsSpeechToTextClient(Api(new StubHttpHandler("""{"text":"hi there"}""")), null);

        var updates = new List<SpeechToTextResponseUpdate>();

        await foreach (var update in client.GetStreamingTextAsync(new MemoryStream([1, 2]), null, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        Assert.Equal("hi there", Assert.Single(updates).Text);
    }

    [Fact]
    public async Task Search_ReadsVoicesFiltersByLanguageAndGender_AndReturnsTheNextCursor()
    {
        var handler = new StubHttpHandler("""
            {"voices":[
              {"voice_id":"a","name":"Ana","preview_url":"https://x/a.mp3","labels":{"gender":"female"},"verified_languages":[{"language":"es"},{"language":"en"}]},
              {"voice_id":"b","name":"Ben","labels":{"gender":"male"},"verified_languages":[{"language":"en"}]},
              {"voice_id":"c","name":"Cy","labels":{"gender":"female"},"verified_languages":[{"language":"fr"}]}],
             "has_more":true,"next_page_token":"tok2"}
            """);
        var library = new ElevenLabsVoiceLibrary(Api(handler));

        var result = await library.SearchAsync(new VoiceSearchRequest { Query = "calm voice", Language = "es-MX", Gender = SpeechVoiceGender.Female, PageSize = 500, Cursor = "tok1" }, TestContext.Current.CancellationToken);

        Assert.Equal("https://api.elevenlabs.io/v2/voices?page_size=100&search=calm%20voice&next_page_token=tok1", handler.Last.RequestUri!.AbsoluteUri);
        var voice = Assert.Single(result.Voices);
        Assert.Equal(("a", "Ana", SpeechVoiceGender.Female, "https://x/a.mp3"), (voice.Id, voice.Name, voice.Gender, voice.VoiceSampleUrl));
        Assert.Equal("tok2", result.NextCursor);
    }

    [Fact]
    public async Task Clone_UploadsTheRecordingsAndReturnsTheNewVoice()
    {
        var handler = new StubHttpHandler("""{"voice_id":"new-id"}""");
        var library = new ElevenLabsVoiceLibrary(Api(handler));

        var voice = await library.CloneAsync(
            new VoiceCloneRequest
            {
                Name = "Support agent",
                Description = "Warm",
                Language = "en",
                Samples = [new VoiceCloneSample { FileName = "a.mp3", ContentType = "audio/mpeg", Content = [1, 2, 3] }],
            },
            TestContext.Current.CancellationToken);

        Assert.True(library.SupportsCloning);
        Assert.Equal("new-id", voice.Id);
        Assert.Equal("Support agent", voice.Name);
        Assert.Equal("https://api.elevenlabs.io/v1/voices/add", handler.Last.RequestUri!.AbsoluteUri);
        Assert.Contains("name=name", handler.LastBody);
        Assert.Contains("filename=a.mp3", handler.LastBody);
        await Assert.ThrowsAsync<ArgumentException>(() => library.CloneAsync(new VoiceCloneRequest { Name = "x" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_RemovesTheVoice()
    {
        var handler = new StubHttpHandler("{}");
        var library = new ElevenLabsVoiceLibrary(Api(handler));

        await library.DeleteAsync("new id", TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Delete, handler.Last.Method);
        Assert.EndsWith("/v1/voices/new%20id", handler.Last.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Provider_HandlesItsNameAndRefusesWhatItCannotDo()
    {
        var provider = new ElevenLabsClientProvider(new FakeHttpClientFactory(new StubHttpHandler("{}")));

        Assert.True(provider.CanHandle("elevenlabs"));
        Assert.False(provider.CanHandle("OpenAI"));
        Assert.True(provider.CanHandle(ElevenLabsConstants.ClientName));
        await Assert.ThrowsAsync<NotSupportedException>(async () => await provider.GetChatClientAsync(new AIProviderConnectionEntry(new Dictionary<string, object>())));
    }

    [Fact]
    public async Task Provider_BuildsClientsFromTheConnectionsKeyAndEndpoint()
    {
        var handler = new StubHttpHandler("""{"voices":[{"voice_id":"a","name":"Ana"}]}""");
        var provider = new ElevenLabsClientProvider(new FakeHttpClientFactory(handler));
        var connection = new AIProviderConnectionEntry(new Dictionary<string, object> { ["ApiKey"] = "k", ["Endpoint"] = "https://api.us.elevenlabs.io" });

        var voices = await provider.GetSpeechVoicesAsync(connection);

        Assert.Equal("a", Assert.Single(voices).Id);
        Assert.StartsWith("https://api.us.elevenlabs.io/v2/voices", handler.Last.RequestUri!.AbsoluteUri);
        Assert.Equal("k", handler.Last.Headers.GetValues("xi-api-key").Single());
        Assert.NotNull(await provider.GetTextToSpeechClientAsync(connection, "eleven_flash_v2_5"));
        Assert.NotNull(await provider.GetSpeechToTextClientAsync(connection, "scribe_v1"));
        Assert.NotNull(provider.GetLibrary(connection));
    }
}
