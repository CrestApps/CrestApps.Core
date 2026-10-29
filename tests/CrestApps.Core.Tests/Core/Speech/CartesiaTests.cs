#pragma warning disable MEAI001 // Speech APIs from Microsoft.Extensions.AI are preview.
#nullable enable
using System.Net;
using System.Text.Json;
using CrestApps.Core.AI.Cartesia;
using CrestApps.Core.AI.Cartesia.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.Tests.Core.Speech;

public sealed class CartesiaTests
{
    private static CartesiaApi Api(StubHttpHandler handler)
        => new(new HttpClient(handler), null, "secret-key");

    [Fact]
    public async Task Streaming_SendsTheVersionedRequestWithVoiceFormatSpeedAndEmotion()
    {
        var handler = StubHttpHandler.Bytes([1, 2]);
        var client = new CartesiaTextToSpeechClient(Api(handler), null);

        await foreach (var _ in client.GetStreamingAudioAsync("Hello there", new TextToSpeechOptions
        {
            VoiceId = "voice-1",
            Language = "es-MX",
            Speed = 3f,
            AudioFormat = "Pcm24000",
            AdditionalProperties = new() { [TextToSpeechOptionsProperties.Emotion] = "Calm" },
        }, TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal("https://api.cartesia.ai/tts/bytes", handler.Last.RequestUri!.AbsoluteUri);
        Assert.Equal("secret-key", handler.Last.Headers.GetValues("X-API-Key").Single());
        Assert.Equal(CartesiaConstants.ApiVersion, handler.Last.Headers.GetValues("Cartesia-Version").Single());

        using var body = JsonDocument.Parse(handler.LastBody);
        var root = body.RootElement;
        Assert.Equal(CartesiaConstants.DefaultTextToSpeechModel, root.GetProperty("model_id").GetString());
        Assert.Equal("Hello there", root.GetProperty("transcript").GetString());
        Assert.Equal("voice-1", root.GetProperty("voice").GetProperty("id").GetString());
        Assert.Equal("id", root.GetProperty("voice").GetProperty("mode").GetString());
        Assert.Equal("es", root.GetProperty("language").GetString());
        Assert.Equal(("raw", "pcm_s16le", 24000), (
            root.GetProperty("output_format").GetProperty("container").GetString(),
            root.GetProperty("output_format").GetProperty("encoding").GetString(),
            root.GetProperty("output_format").GetProperty("sample_rate").GetInt32()));
        Assert.Equal(1.5f, root.GetProperty("generation_config").GetProperty("speed").GetSingle());
        Assert.Equal("calm", root.GetProperty("generation_config").GetProperty("emotion").GetString());
    }

    [Fact]
    public async Task Streaming_DefaultsToEnglishAndMp3_WithoutGenerationSettings()
    {
        var handler = StubHttpHandler.Bytes([1, 2]);
        var client = new CartesiaTextToSpeechClient(Api(handler), "sonic-3");

        var updates = new List<TextToSpeechResponseUpdate>();

        await foreach (var update in client.GetStreamingAudioAsync("Hi", new TextToSpeechOptions { VoiceId = "v" }, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        var root = JsonDocument.Parse(handler.LastBody).RootElement;
        Assert.Equal("sonic-3", root.GetProperty("model_id").GetString());
        Assert.Equal("en", root.GetProperty("language").GetString());
        Assert.Equal("mp3", root.GetProperty("output_format").GetProperty("container").GetString());
        Assert.False(root.TryGetProperty("generation_config", out _));
        Assert.Equal("audio/mpeg", Assert.IsType<DataContent>(updates[0].Contents[0]).MediaType);
    }

    [Fact]
    public async Task Streaming_NeverSplitsA16BitSampleAcrossChunks()
    {
        var handler = StubHttpHandler.Bytes([1, 2, 3], [4, 5, 6, 7, 8], [9, 10, 11, 12]);
        var client = new CartesiaTextToSpeechClient(Api(handler), null);

        var chunks = new List<byte[]>();

        await foreach (var update in client.GetStreamingAudioAsync("Hi", new TextToSpeechOptions { VoiceId = "v", AudioFormat = "Pcm16000" }, TestContext.Current.CancellationToken))
        {
            chunks.Add(Assert.IsType<DataContent>(update.Contents[0]).Data.ToArray());
        }

        Assert.All(chunks, chunk => Assert.Equal(0, chunk.Length % 2));
        Assert.Equal(Enumerable.Range(1, 12).Select(i => (byte)i), chunks.SelectMany(c => c));
    }

    [Fact]
    public async Task NeutralMarkup_IsRenderedAsCartesiaTags()
    {
        var handler = StubHttpHandler.Bytes([1, 2]);
        var client = new CartesiaTextToSpeechClient(Api(handler), null);

        await client.GetAudioAsync("Wait<break time=\"1s\"/>then <spell>AB1</spell>", new TextToSpeechOptions { VoiceId = "v" }, TestContext.Current.CancellationToken);

        Assert.Equal("Wait<break time=\"1.0s\"/>then <spell>AB1</spell>", JsonDocument.Parse(handler.LastBody).RootElement.GetProperty("transcript").GetString());
    }

    [Fact]
    public async Task Refuses_ARequestWithoutAVoice_AndARateItCannotProduce()
    {
        var client = new CartesiaTextToSpeechClient(Api(StubHttpHandler.Bytes([1, 2])), null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAudioAsync("Hi", null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetAudioAsync("Hi", new TextToSpeechOptions { VoiceId = "v", AudioFormat = "Pcm32000" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnErrorAnswer_SaysWhatWentWrong()
    {
        var client = new CartesiaTextToSpeechClient(Api(new StubHttpHandler("""{"error":"bad key"}""", HttpStatusCode.TooManyRequests)), null);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAudioAsync("Hi", new TextToSpeechOptions { VoiceId = "v" }, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Contains("rate limiting", error.Message);
        Assert.DoesNotContain("secret-key", error.Message);
    }

    [Fact]
    public async Task SpeechToText_UploadsRawPcmAsWavAndReadsTheTranscript()
    {
        var handler = new StubHttpHandler("""{"text":" hello world ","language":"en","duration":1.2}""");
        var client = new CartesiaSpeechToTextClient(Api(handler), null);

        var response = await client.GetTextAsync(new MemoryStream([1, 2, 3, 4]), new SpeechToTextOptions { SpeechLanguage = "en-US", SpeechSampleRate = 16000 }, TestContext.Current.CancellationToken);

        Assert.Equal("hello world", response.Text);
        Assert.Equal("https://api.cartesia.ai/stt", handler.Last.RequestUri!.AbsoluteUri);
        Assert.Contains(CartesiaConstants.DefaultSpeechToTextModel, handler.LastBody);
        Assert.Contains("name=language", handler.LastBody);
        Assert.Contains("filename=audio.wav", handler.LastBody);
        Assert.Contains("RIFF", handler.LastBody);
    }

    [Fact]
    public async Task Search_ReadsVoicesFiltersByLanguage_AndReturnsTheNextCursor()
    {
        var handler = new StubHttpHandler("""
            {"data":[
              {"id":"a","name":"Ana","language":"es","gender":"feminine"},
              {"id":"b","name":"Ben","language":"en","gender":"masculine"}],
             "has_more":true,"next_page":"cursor-2"}
            """);
        var library = new CartesiaVoiceLibrary(Api(handler));

        var result = await library.SearchAsync(new VoiceSearchRequest { Query = "warm", Language = "es-MX", Gender = SpeechVoiceGender.Female, PageSize = 10, Cursor = "cursor-1" }, TestContext.Current.CancellationToken);

        Assert.Equal("https://api.cartesia.ai/voices?limit=10&q=warm&gender=feminine&starting_after=cursor-1", handler.Last.RequestUri!.AbsoluteUri);
        var voice = Assert.Single(result.Voices);
        Assert.Equal(("a", "Ana", SpeechVoiceGender.Female), (voice.Id, voice.Name, voice.Gender));
        Assert.Equal("cursor-2", result.NextCursor);
    }

    [Fact]
    public async Task Clone_UploadsTheFirstClipWithItsLanguageAndReturnsTheNewVoice()
    {
        var handler = new StubHttpHandler("""{"id":"new-id","name":"Support agent","language":"en","gender":"feminine"}""");
        var library = new CartesiaVoiceLibrary(Api(handler));

        var voice = await library.CloneAsync(
            new VoiceCloneRequest
            {
                Name = "Support agent",
                Language = "en-US",
                Samples = [new VoiceCloneSample { FileName = "a.wav", ContentType = "audio/wav", Content = [1, 2, 3] }],
            },
            TestContext.Current.CancellationToken);

        Assert.True(library.SupportsCloning);
        Assert.Equal(("new-id", SpeechVoiceGender.Female), (voice.Id, voice.Gender));
        Assert.Equal("https://api.cartesia.ai/voices/clone", handler.Last.RequestUri!.AbsoluteUri);
        Assert.Contains("name=clip", handler.LastBody);
        Assert.Contains("filename=a.wav", handler.LastBody);
        await Assert.ThrowsAsync<ArgumentException>(() => library.CloneAsync(new VoiceCloneRequest { Name = "x" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_RemovesTheVoice()
    {
        var handler = new StubHttpHandler("{}");

        await new CartesiaVoiceLibrary(Api(handler)).DeleteAsync("new id", TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Delete, handler.Last.Method);
        Assert.EndsWith("/voices/new%20id", handler.Last.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Provider_BuildsClientsFromTheConnection()
    {
        var handler = new StubHttpHandler("""{"data":[{"id":"a","name":"Ana"}]}""");
        var provider = new CartesiaClientProvider(new FakeHttpClientFactory(handler));
        var connection = new AIProviderConnectionEntry(new Dictionary<string, object> { ["ApiKey"] = "k" });

        var voices = await provider.GetSpeechVoicesAsync(connection);

        Assert.True(provider.CanHandle("cartesia"));
        Assert.False(provider.CanHandle("ElevenLabs"));
        Assert.Equal("a", Assert.Single(voices).Id);
        Assert.Equal("k", handler.Last.Headers.GetValues("X-API-Key").Single());
        Assert.NotNull(await provider.GetTextToSpeechClientAsync(connection, "sonic-2"));
        Assert.NotNull(await provider.GetSpeechToTextClientAsync(connection, "ink-whisper"));
        await Assert.ThrowsAsync<NotSupportedException>(async () => await provider.GetChatClientAsync(connection));
    }
}
