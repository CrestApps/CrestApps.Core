#pragma warning disable MEAI001
using System.Net;
using CrestApps.Core.AI.Resilience;
using CrestApps.Core.AI.Resilience.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Polly;
using Polly.Retry;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AIClientBuilderResilienceExtensionsTests
{
    private static readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void AIChatClientRetryOptions_DefaultsMatchDocumentedSchedule()
    {
        var options = new AIChatClientRetryOptions();

        Assert.Equal(5, options.MaxRateLimitRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), options.RateLimitRetryDelay);
        Assert.Equal(DelayBackoffType.Exponential, options.BackoffType);
        Assert.True(options.UseJitter);
        Assert.Equal(TimeSpan.FromSeconds(32), options.MaxRetryDelay);
    }

    [Fact]
    public async Task UseDefaultResilience_WhenProviderReturns429_RetriesAndSucceeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
        var innerClient = new Mock<IChatClient>(MockBehavior.Strict);
        innerClient
            .SetupSequence(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateRateLimitException())
            .ThrowsAsync(CreateRateLimitException())
            .ReturnsAsync(response);

        var client = innerClient.Object
            .AsBuilder()
            .UseDefaultResilience(options => options.RateLimitRetryDelay = TimeSpan.Zero)
            .Build(_serviceProvider);

        var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: cancellationToken);

        Assert.Equal("done", result.Text);
        innerClient.Verify(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task UseDefaultResilience_WhenAppliedToStreamingChat_RetriesAndSucceeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var innerClient = new Mock<IChatClient>(MockBehavior.Strict);
        innerClient
            .SetupSequence(client => client.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowChatFailure())
            .Returns(CreateChatUpdates());

        var client = innerClient.Object
            .AsBuilder()
            .UseDefaultResilience(options => options.RateLimitRetryDelay = TimeSpan.Zero)
            .Build(_serviceProvider);

        var updates = await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: cancellationToken).ToListAsync(cancellationToken);

        Assert.Single(updates);
        Assert.Equal("done", updates[0].Text);
        innerClient.Verify(client => client.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UseDefaultResilience_WhenNotApplied_DoesNotRetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var innerClient = new Mock<IChatClient>(MockBehavior.Strict);
        innerClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateRateLimitException());

        await Assert.ThrowsAsync<HttpRequestException>(() => innerClient.Object.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: cancellationToken));

        innerClient.Verify(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseResilience_WhenCustomPipelineConfigured_RetriesUsingBuilderPolicy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
        var innerClient = new Mock<IChatClient>(MockBehavior.Strict);
        innerClient
            .SetupSequence(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateRateLimitException())
            .ReturnsAsync(response);

        var client = innerClient.Object
            .AsBuilder()
            .UseResilience(pipeline => pipeline.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is { } ex &&
                    AIProviderErrorHelper.IsRateLimitException(ex)),
            }))
            .Build(_serviceProvider);

        var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: cancellationToken);

        Assert.Equal("done", result.Text);
        innerClient.Verify(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UseResilience_WhenPrebuiltPipelineProvided_AppliesRetryPolicy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
        var innerClient = new Mock<IChatClient>(MockBehavior.Strict);
        innerClient
            .SetupSequence(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Retry me."))
            .ReturnsAsync(response);

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is InvalidOperationException),
            })
            .Build();

        var client = innerClient.Object
            .AsBuilder()
            .UseResilience(pipeline)
            .Build(_serviceProvider);

        var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: cancellationToken);

        Assert.Equal("done", result.Text);
        innerClient.Verify(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UseDefaultResilience_WhenRetriesExhausted_ThrowsFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var innerClient = new Mock<IChatClient>(MockBehavior.Strict);
        innerClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateRateLimitException());

        var client = innerClient.Object
            .AsBuilder()
            .UseDefaultResilience(options =>
            {
                options.MaxRateLimitRetries = 4;
                options.RateLimitRetryDelay = TimeSpan.Zero;
            })
            .Build(_serviceProvider);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: cancellationToken));

        innerClient.Verify(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    [Fact]
    public async Task UseResilience_WhenCustomPolicyDoesNotHandleFailure_DoesNotRetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var innerClient = new Mock<IChatClient>(MockBehavior.Strict);
        innerClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Do not retry."));

        var client = innerClient.Object
            .AsBuilder()
            .UseResilience(pipeline => pipeline.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is HttpRequestException),
            }))
            .Build(_serviceProvider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: cancellationToken));

        innerClient.Verify(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseDefaultResilience_WhenAppliedToEmbeddingGenerator_RetriesAndSucceeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = new GeneratedEmbeddings<Embedding<float>>
        {
            new Embedding<float>(new[] { 1F, 2F }),
        };
        var innerGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);
        innerGenerator
            .SetupSequence(generator => generator.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateRateLimitException())
            .ReturnsAsync(response);

        var generator = innerGenerator.Object
            .AsBuilder()
            .UseDefaultResilience(options => options.RateLimitRetryDelay = TimeSpan.Zero)
            .Build(_serviceProvider);

        var result = await generator.GenerateAsync(["hello"], cancellationToken: cancellationToken);

        Assert.Single(result);
        innerGenerator.Verify(generator => generator.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UseDefaultResilience_WhenAppliedToImageGenerator_RetriesAndSucceeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = new ImageGenerationResponse(new List<AIContent>
        {
            new DataContent(new byte[] { 1, 2, 3 }, "image/png"),
        });
        var innerGenerator = new Mock<IImageGenerator>(MockBehavior.Strict);
        innerGenerator
            .SetupSequence(generator => generator.GenerateAsync(It.IsAny<ImageGenerationRequest>(), It.IsAny<ImageGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateRateLimitException())
            .ReturnsAsync(response);

        var generator = innerGenerator.Object
            .AsBuilder()
            .UseDefaultResilience(options => options.RateLimitRetryDelay = TimeSpan.Zero)
            .Build(_serviceProvider);

        var result = await generator.GenerateAsync(new ImageGenerationRequest("draw a chart"), cancellationToken: cancellationToken);

        Assert.Single(result.Contents);
        innerGenerator.Verify(generator => generator.GenerateAsync(It.IsAny<ImageGenerationRequest>(), It.IsAny<ImageGenerationOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UseResilience_WhenAppliedToImageGeneratorWithPrebuiltPipeline_AppliesRetryPolicy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = new ImageGenerationResponse(new List<AIContent>
        {
            new DataContent(new byte[] { 1, 2, 3 }, "image/png"),
        });
        var innerGenerator = new Mock<IImageGenerator>(MockBehavior.Strict);
        innerGenerator
            .SetupSequence(generator => generator.GenerateAsync(It.IsAny<ImageGenerationRequest>(), It.IsAny<ImageGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Retry me."))
            .ReturnsAsync(response);

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is InvalidOperationException),
            })
            .Build();

        var generator = innerGenerator.Object
            .AsBuilder()
            .UseResilience(pipeline)
            .Build(_serviceProvider);

        var result = await generator.GenerateAsync(new ImageGenerationRequest("draw a chart"), cancellationToken: cancellationToken);

        Assert.Single(result.Contents);
        innerGenerator.Verify(generator => generator.GenerateAsync(It.IsAny<ImageGenerationRequest>(), It.IsAny<ImageGenerationOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UseDefaultResilience_WhenAppliedToSpeechToTextClient_RetriesAndSucceeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = new SpeechToTextResponse("recognized text");
        var innerClient = new Mock<ISpeechToTextClient>(MockBehavior.Strict);
        innerClient
            .SetupSequence(client => client.GetTextAsync(It.IsAny<Stream>(), It.IsAny<SpeechToTextOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateRateLimitException())
            .ReturnsAsync(response);

        var client = innerClient.Object
            .AsBuilder()
            .UseDefaultResilience(options => options.RateLimitRetryDelay = TimeSpan.Zero)
            .Build(_serviceProvider);

        await using var audioStream = new MemoryStream([1, 2, 3]);
        var result = await client.GetTextAsync(audioStream, cancellationToken: cancellationToken);

        Assert.Equal("recognized text", result.Text);
        innerClient.Verify(client => client.GetTextAsync(It.IsAny<Stream>(), It.IsAny<SpeechToTextOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UseDefaultResilience_WhenAppliedToSpeechToTextClientWithNonSeekableStream_ReplaysBufferedAudio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var attempts = new List<byte[]>();
        var innerClient = new Mock<ISpeechToTextClient>(MockBehavior.Strict);
        innerClient
            .Setup(client => client.GetTextAsync(It.IsAny<Stream>(), It.IsAny<SpeechToTextOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (Stream stream, SpeechToTextOptions _, CancellationToken token) =>
            {
                attempts.Add(await ReadAllBytesAsync(stream, token));

                if (attempts.Count == 1)
                {
                    throw CreateRateLimitException();
                }

                return new SpeechToTextResponse("recognized text");
            });

        var client = innerClient.Object
            .AsBuilder()
            .UseDefaultResilience(options => options.RateLimitRetryDelay = TimeSpan.Zero)
            .Build(_serviceProvider);

        await using var audioStream = new NonSeekableReadStream([1, 2, 3, 4]);
        var result = await client.GetTextAsync(audioStream, cancellationToken: cancellationToken);

        Assert.Equal("recognized text", result.Text);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, attempts[0]);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, attempts[1]);
        innerClient.Verify(client => client.GetTextAsync(It.IsAny<Stream>(), It.IsAny<SpeechToTextOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UseResilience_WhenAppliedToStreamingSpeechToTextWithSeekableStream_RetriesAndSucceeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var attempts = new List<byte[]>();
        var innerClient = new Mock<ISpeechToTextClient>(MockBehavior.Strict);
        innerClient
            .Setup(client => client.GetStreamingTextAsync(It.IsAny<Stream>(), It.IsAny<SpeechToTextOptions>(), It.IsAny<CancellationToken>()))
            .Returns((Stream stream, SpeechToTextOptions _, CancellationToken token) => CreateStreamingSpeechAttempt(stream, attempts, token));

        var client = innerClient.Object
            .AsBuilder()
            .UseResilience(pipeline => pipeline.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is HttpRequestException),
            }))
            .Build(_serviceProvider);

        await using var audioStream = new MemoryStream([4, 5, 6]);
        var updates = await client.GetStreamingTextAsync(audioStream, cancellationToken: cancellationToken).ToListAsync(cancellationToken);

        Assert.Single(updates);
        Assert.Equal("recognized text", updates[0].Text);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(new byte[] { 4, 5, 6 }, attempts[0]);
        Assert.Equal(new byte[] { 4, 5, 6 }, attempts[1]);
        innerClient.Verify(client => client.GetStreamingTextAsync(It.IsAny<Stream>(), It.IsAny<SpeechToTextOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UseResilience_WhenAppliedToStreamingSpeechToTextWithNonSeekableStream_DoesNotRetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var innerClient = new Mock<ISpeechToTextClient>(MockBehavior.Strict);
        innerClient
            .Setup(client => client.GetStreamingTextAsync(It.IsAny<Stream>(), It.IsAny<SpeechToTextOptions>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowSpeechToTextFailure());

        var client = innerClient.Object
            .AsBuilder()
            .UseResilience(pipeline => pipeline.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is HttpRequestException),
            }))
            .Build(_serviceProvider);

        await using var audioStream = new NonSeekableReadStream([7, 8, 9]);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.GetStreamingTextAsync(audioStream, cancellationToken: cancellationToken).ToListAsync(cancellationToken));

        innerClient.Verify(client => client.GetStreamingTextAsync(It.IsAny<Stream>(), It.IsAny<SpeechToTextOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseDefaultResilience_WhenAppliedToTextToSpeechClient_RetriesAndSucceeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var response = new TextToSpeechResponse(new List<AIContent>
        {
            new DataContent(new byte[] { 1, 2, 3 }, "audio/wav"),
        });
        var innerClient = new Mock<ITextToSpeechClient>(MockBehavior.Strict);
        innerClient
            .SetupSequence(client => client.GetAudioAsync(It.IsAny<string>(), It.IsAny<TextToSpeechOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateRateLimitException())
            .ReturnsAsync(response);

        var client = innerClient.Object
            .AsBuilder()
            .UseDefaultResilience(options => options.RateLimitRetryDelay = TimeSpan.Zero)
            .Build(_serviceProvider);

        var result = await client.GetAudioAsync("hello", cancellationToken: cancellationToken);

        Assert.Single(result.Contents);
        innerClient.Verify(client => client.GetAudioAsync(It.IsAny<string>(), It.IsAny<TextToSpeechOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UseResilience_WhenAppliedToTextToSpeechStreaming_RetriesUsingConfiguredPolicy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var innerClient = new Mock<ITextToSpeechClient>(MockBehavior.Strict);
        innerClient
            .SetupSequence(client => client.GetStreamingAudioAsync(It.IsAny<string>(), It.IsAny<TextToSpeechOptions>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowTextToSpeechFailure())
            .Returns(CreateTextToSpeechUpdates());

        var client = innerClient.Object
            .AsBuilder()
            .UseResilience(pipeline => pipeline.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
                ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is HttpRequestException),
            }))
            .Build(_serviceProvider);

        var updates = await client.GetStreamingAudioAsync("hello", cancellationToken: cancellationToken).ToListAsync(cancellationToken);

        Assert.Single(updates);
        innerClient.Verify(client => client.GetStreamingAudioAsync(It.IsAny<string>(), It.IsAny<TextToSpeechOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void UseDefaultResilience_WhenAppliedToEmbeddingGenerator_ForwardsGetServiceAndDispose()
    {
        var sentinel = new object();
        var innerGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);
        innerGenerator
            .Setup(generator => generator.GetService(typeof(string), "service-key"))
            .Returns(sentinel);
        innerGenerator
            .Setup(generator => generator.Dispose());

        var generator = innerGenerator.Object
            .AsBuilder()
            .UseDefaultResilience()
            .Build(_serviceProvider);

        var service = ((IEmbeddingGenerator)generator).GetService(typeof(string), "service-key");
        generator.Dispose();

        Assert.Same(sentinel, service);
        innerGenerator.Verify(generator => generator.GetService(typeof(string), "service-key"), Times.Once);
        innerGenerator.Verify(generator => generator.Dispose(), Times.Once);
    }

    [Fact]
    public void UseDefaultResilience_WhenAppliedToSpeechToTextClient_ForwardsGetServiceAndDispose()
    {
        var sentinel = new object();
        var innerClient = new Mock<ISpeechToTextClient>(MockBehavior.Strict);
        innerClient
            .Setup(client => client.GetService(typeof(string), "service-key"))
            .Returns(sentinel);
        innerClient
            .Setup(client => client.Dispose());

        var client = innerClient.Object
            .AsBuilder()
            .UseDefaultResilience()
            .Build(_serviceProvider);

        var service = client.GetService(typeof(string), "service-key");
        client.Dispose();

        Assert.Same(sentinel, service);
        innerClient.Verify(client => client.GetService(typeof(string), "service-key"), Times.Once);
        innerClient.Verify(client => client.Dispose(), Times.Once);
    }

    [Fact]
    public void UseDefaultResilience_WhenAppliedToTextToSpeechClient_ForwardsGetServiceAndDispose()
    {
        var sentinel = new object();
        var innerClient = new Mock<ITextToSpeechClient>(MockBehavior.Strict);
        innerClient
            .Setup(client => client.GetService(typeof(string), "service-key"))
            .Returns(sentinel);
        innerClient
            .Setup(client => client.Dispose());

        var client = innerClient.Object
            .AsBuilder()
            .UseDefaultResilience()
            .Build(_serviceProvider);

        var service = client.GetService(typeof(string), "service-key");
        client.Dispose();

        Assert.Same(sentinel, service);
        innerClient.Verify(client => client.GetService(typeof(string), "service-key"), Times.Once);
        innerClient.Verify(client => client.Dispose(), Times.Once);
    }

    private static HttpRequestException CreateRateLimitException()
    {
        return new HttpRequestException("Too many requests.", null, HttpStatusCode.TooManyRequests);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);

        return copy.ToArray();
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ThrowChatFailure()
    {
        await Task.Yield();
        throw CreateRateLimitException();
#pragma warning disable CS0162
        yield return null;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateChatUpdates()
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<SpeechToTextResponseUpdate> CreateStreamingSpeechAttempt(
        Stream stream,
        List<byte[]> attempts,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        attempts.Add(await ReadAllBytesAsync(stream, cancellationToken));

        if (attempts.Count == 1)
        {
            throw CreateRateLimitException();
        }

        yield return new SpeechToTextResponseUpdate("recognized text");
    }

    private static async IAsyncEnumerable<SpeechToTextResponseUpdate> ThrowSpeechToTextFailure()
    {
        await Task.Yield();
        throw CreateRateLimitException();
#pragma warning disable CS0162
        yield return null;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<TextToSpeechResponseUpdate> ThrowTextToSpeechFailure()
    {
        await Task.Yield();
        throw CreateRateLimitException();
#pragma warning disable CS0162
        yield return null;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<TextToSpeechResponseUpdate> CreateTextToSpeechUpdates()
    {
        yield return new TextToSpeechResponseUpdate(new List<AIContent>
        {
            new DataContent(new byte[] { 1, 2, 3 }, "audio/wav"),
        });
        await Task.CompletedTask;
    }

    private sealed class NonSeekableReadStream : MemoryStream
    {
        public NonSeekableReadStream(byte[] buffer)
            : base(buffer)
        {
        }

        public override bool CanSeek => false;

        public override long Position
        {
            get => base.Position;
            set => throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin loc)
        {
            throw new NotSupportedException();
        }
    }
}
#pragma warning restore MEAI001
