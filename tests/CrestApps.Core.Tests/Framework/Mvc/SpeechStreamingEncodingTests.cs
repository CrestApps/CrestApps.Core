using System.Threading.Channels;
using CrestApps.Core.AI.Chat.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

#pragma warning disable MEAI001
namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class SpeechStreamingEncodingTests
{
    [Fact]
    public async Task AIChatHubCore_StreamSpeechAsync_PreservesChunkPayloadsOrderAndMediaTypes()
    {
        var events = new List<AudioEvent>();
        var hub = new TestAIChatHub
        {
            Clients = CreateAIChatClients(events),
        };

        await VerifyStreamSpeechAsync(hub.StreamSpeechForTestAsync, events);
    }

    [Fact]
    public async Task ChatInteractionHubBase_StreamSpeechAsync_PreservesChunkPayloadsOrderAndMediaTypes()
    {
        var events = new List<AudioEvent>();
        var hub = new TestChatInteractionHub
        {
            Clients = CreateChatInteractionClients(events),
        };

        await VerifyStreamSpeechAsync(hub.StreamSpeechForTestAsync, events);
    }

    [Fact]
    public async Task AIChatHubCore_StreamSentencesAsSpeechAsync_PreservesChunkAndCompletionOrder()
    {
        var events = new List<AudioEvent>();
        var hub = new TestAIChatHub
        {
            Clients = CreateAIChatClients(events),
        };

        await VerifyStreamSentencesAsSpeechAsync(hub.StreamSentencesAsSpeechForTestAsync, events);
    }

    [Fact]
    public async Task ChatInteractionHubBase_StreamSentencesAsSpeechAsync_PreservesChunkAndCompletionOrder()
    {
        var events = new List<AudioEvent>();
        var hub = new TestChatInteractionHub
        {
            Clients = CreateChatInteractionClients(events),
        };

        await VerifyStreamSentencesAsSpeechAsync(hub.StreamSentencesAsSpeechForTestAsync, events);
    }

    private static async Task VerifyStreamSpeechAsync(
        Func<ITextToSpeechClient, string, string, string, CancellationToken, Task> streamSpeechAsync,
        List<AudioEvent> events)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var chunks = CreateMixedAudioChunks();
        var client = new Mock<ITextToSpeechClient>(MockBehavior.Strict);
        client
            .Setup(textToSpeechClient => textToSpeechClient.GetStreamingAudioAsync(
                It.IsAny<string>(),
                It.IsAny<TextToSpeechOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(StreamUpdates(chunks.Select(chunk => chunk.Content)));

        await streamSpeechAsync(client.Object, "turn-1", "Hello world.", "voice-1", cancellationToken);

        var expected = chunks
            .Where(chunk => chunk.ExpectedData.Length > 0)
            .Select(chunk => AudioEvent.Chunk(
                "turn-1",
                Convert.ToBase64String(chunk.ExpectedData.ToArray()),
                chunk.ExpectedContentType))
            .Append(AudioEvent.Complete("turn-1"));

        Assert.Equal(expected, events);
        client.Verify(textToSpeechClient => textToSpeechClient.GetStreamingAudioAsync(
            "Hello world.",
            It.Is<TextToSpeechOptions>(options => options.VoiceId == "voice-1"),
            cancellationToken), Times.Once);
    }

    private static async Task VerifyStreamSentencesAsSpeechAsync(
        Func<ITextToSpeechClient, Func<string>, ChannelReader<string>, string, CancellationToken, Task> streamSpeechAsync,
        List<AudioEvent> events)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstChunk = new byte[] { 0, 16, 32, 255 };
        var secondChunkBuffer = new byte[] { 91, 92, 0, 128, 255, 64, 93 };
        var secondChunk = secondChunkBuffer.AsMemory(2, 4);
        var client = new Mock<ITextToSpeechClient>(MockBehavior.Strict);
        client
            .SetupSequence(textToSpeechClient => textToSpeechClient.GetStreamingAudioAsync(
                It.IsAny<string>(),
                It.IsAny<TextToSpeechOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(StreamUpdates(
                [
                    new DataContent(ReadOnlyMemory<byte>.Empty, "audio/empty"),
                    new DataContent(firstChunk, "audio/aac"),
                ]))
            .Returns(StreamUpdates(
                [
                    new DataContent(secondChunk, "audio/opus"),
                ]));

        var sentenceChannel = Channel.CreateUnbounded<string>();
        await sentenceChannel.Writer.WriteAsync("First sentence.", cancellationToken);
        await sentenceChannel.Writer.WriteAsync("Second sentence.", cancellationToken);
        sentenceChannel.Writer.Complete();
        var identifiers = new Queue<string>(["turn-1", "turn-2"]);

        await streamSpeechAsync(
            client.Object,
            identifiers.Dequeue,
            sentenceChannel.Reader,
            "voice-2",
            cancellationToken);

        Assert.Equal(
        [
            AudioEvent.Chunk("turn-1", Convert.ToBase64String(firstChunk), "audio/aac"),
            AudioEvent.Complete("turn-1"),
            AudioEvent.Chunk("turn-2", Convert.ToBase64String(secondChunk.ToArray()), "audio/opus"),
            AudioEvent.Complete("turn-2"),
        ], events);
        client.Verify(textToSpeechClient => textToSpeechClient.GetStreamingAudioAsync(
            "First sentence.",
            It.Is<TextToSpeechOptions>(options => options.VoiceId == "voice-2"),
            cancellationToken), Times.Once);
        client.Verify(textToSpeechClient => textToSpeechClient.GetStreamingAudioAsync(
            "Second sentence.",
            It.Is<TextToSpeechOptions>(options => options.VoiceId == "voice-2"),
            cancellationToken), Times.Once);
    }

    private static IReadOnlyList<AudioChunkCase> CreateMixedAudioChunks()
    {
        var fullChunk = new byte[] { 0, 1, 2, 127, 128, 254, 255 };
        var slicedBuffer = new byte[] { 90, 91, 0, 255, 17, 42, 92, 93 };
        var slicedChunk = slicedBuffer.AsMemory(2, 4);
        var randomChunk = new byte[257];
        new Random(42).NextBytes(randomChunk);
        randomChunk[0] = 0;
        randomChunk[^1] = 255;

        return
        [
            new(
                new DataContent(ReadOnlyMemory<byte>.Empty, "audio/empty"),
                ReadOnlyMemory<byte>.Empty,
                "audio/empty"),
            new(
                new DataContent(fullChunk, "audio/wav"),
                fullChunk,
                "audio/wav"),
            new(
                new DataContent(slicedChunk, "audio/ogg"),
                slicedChunk,
                "audio/ogg"),
            new(
                new DataContent(randomChunk, "application/octet-stream"),
                randomChunk,
                "application/octet-stream"),
            new(
                new DataContent(new Uri("data:;base64,/w=="), null),
                new byte[] { 255 },
                "text/plain;charset=US-ASCII"),
        ];
    }

    private static async IAsyncEnumerable<TextToSpeechResponseUpdate> StreamUpdates(
        IEnumerable<DataContent> contents)
    {
        foreach (var content in contents)
        {
            yield return new TextToSpeechResponseUpdate([content]);
        }

        await Task.CompletedTask;
    }

    private static IHubCallerClients<IAIChatHubClient> CreateAIChatClients(List<AudioEvent> events)
    {
        var caller = new Mock<IAIChatHubClient>(MockBehavior.Strict);
        caller
            .Setup(client => client.ReceiveAudioChunk(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string, string>((identifier, payload, contentType) =>
                events.Add(AudioEvent.Chunk(identifier, payload, contentType)))
            .Returns(Task.CompletedTask);
        caller
            .Setup(client => client.ReceiveAudioComplete(It.IsAny<string>()))
            .Callback<string>(identifier => events.Add(AudioEvent.Complete(identifier)))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients<IAIChatHubClient>>(MockBehavior.Strict);
        clients.SetupGet(hubClients => hubClients.Caller).Returns(caller.Object);

        return clients.Object;
    }

    private static IHubCallerClients<IChatInteractionHubClient> CreateChatInteractionClients(
        List<AudioEvent> events)
    {
        var caller = new Mock<IChatInteractionHubClient>(MockBehavior.Strict);
        caller
            .Setup(client => client.ReceiveAudioChunk(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string, string>((identifier, payload, contentType) =>
                events.Add(AudioEvent.Chunk(identifier, payload, contentType)))
            .Returns(Task.CompletedTask);
        caller
            .Setup(client => client.ReceiveAudioComplete(It.IsAny<string>()))
            .Callback<string>(identifier => events.Add(AudioEvent.Complete(identifier)))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients<IChatInteractionHubClient>>(MockBehavior.Strict);
        clients.SetupGet(hubClients => hubClients.Caller).Returns(caller.Object);

        return clients.Object;
    }

    private sealed class TestAIChatHub : AIChatHubCore<IAIChatHubClient>
    {
        public TestAIChatHub()
            : base(
                new ServiceCollection().BuildServiceProvider(),
                TimeProvider.System,
                NullLogger.Instance)
        {
        }

        public Task StreamSpeechForTestAsync(
            ITextToSpeechClient textToSpeechClient,
            string identifier,
            string text,
            string voiceName,
            CancellationToken cancellationToken)
        {
            return StreamSpeechAsync(
                textToSpeechClient,
                identifier,
                text,
                voiceName,
                cancellationToken);
        }

        public Task StreamSentencesAsSpeechForTestAsync(
            ITextToSpeechClient textToSpeechClient,
            Func<string> getIdentifier,
            ChannelReader<string> sentenceReader,
            string voiceName,
            CancellationToken cancellationToken)
        {
            return StreamSentencesAsSpeechAsync(
                textToSpeechClient,
                getIdentifier,
                sentenceReader,
                voiceName,
                cancellationToken);
        }
    }

    private sealed class TestChatInteractionHub : ChatInteractionHubBase
    {
        public TestChatInteractionHub()
            : base(
                new ServiceCollection().BuildServiceProvider(),
                TimeProvider.System,
                NullLogger.Instance)
        {
        }

        public Task StreamSpeechForTestAsync(
            ITextToSpeechClient textToSpeechClient,
            string identifier,
            string text,
            string voiceName,
            CancellationToken cancellationToken)
        {
            return StreamSpeechAsync(
                textToSpeechClient,
                identifier,
                text,
                voiceName,
                cancellationToken);
        }

        public Task StreamSentencesAsSpeechForTestAsync(
            ITextToSpeechClient textToSpeechClient,
            Func<string> getIdentifier,
            ChannelReader<string> sentenceReader,
            string voiceName,
            CancellationToken cancellationToken)
        {
            return StreamSentencesAsSpeechAsync(
                textToSpeechClient,
                getIdentifier,
                sentenceReader,
                voiceName,
                cancellationToken);
        }
    }

    private sealed record AudioChunkCase(
        DataContent Content,
        ReadOnlyMemory<byte> ExpectedData,
        string ExpectedContentType);

    private sealed record AudioEvent(
        string EventName,
        string Identifier,
        string Payload,
        string ContentType)
    {
        public static AudioEvent Chunk(
            string identifier,
            string payload,
            string contentType)
        {
            return new("ReceiveAudioChunk", identifier, payload, contentType);
        }

        public static AudioEvent Complete(string identifier)
        {
            return new("ReceiveAudioComplete", identifier, null, null);
        }
    }
}
