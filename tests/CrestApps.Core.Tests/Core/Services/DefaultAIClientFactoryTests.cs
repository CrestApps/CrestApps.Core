#pragma warning disable MEAI001
using System.Net;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Resilience;
using CrestApps.Core.AI.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class DefaultAIClientFactoryTests
{
    [Fact]
    public async Task CreateChatClientAsync_WhenPipelineConfigured_AppliesBuilderMiddleware()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
        var innerClient = new Mock<IChatClient>(MockBehavior.Strict);
        innerClient
            .SetupSequence(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateRateLimitException())
            .ReturnsAsync(response);

        var deployment = new AIDeployment
        {
            ClientName = "Test",
            ModelName = "chat-model",
        };
        var provider = new Mock<IAIClientProvider>(MockBehavior.Strict);
        provider.Setup(p => p.CanHandle("Test")).Returns(true);
        provider.Setup(p => p.GetChatClientAsync(It.IsAny<AIProviderConnectionEntry>(), "chat-model")).Returns(new ValueTask<IChatClient>(innerClient.Object));

        var factory = CreateFactory(provider.Object);
        var client = await factory.CreateChatClientAsync(
            deployment,
            builder => builder.UseDefaultResilience(options => options.RateLimitRetryDelay = TimeSpan.Zero));

        var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("done", result.Text);
        innerClient.Verify(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CreateEmbeddingGeneratorAsync_WhenPipelineConfigured_AppliesBuilderMiddleware()
    {
        var response = new GeneratedEmbeddings<Embedding<float>>
        {
            new(new[] { 1F, 2F }),
        };
        var innerGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);
        innerGenerator
            .SetupSequence(generator => generator.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateRateLimitException())
            .ReturnsAsync(response);

        var deployment = new AIDeployment
        {
            ClientName = "Test",
            ModelName = "embedding-model",
        };
        var provider = new Mock<IAIClientProvider>(MockBehavior.Strict);
        provider.Setup(p => p.CanHandle("Test")).Returns(true);
        provider.Setup(p => p.GetEmbeddingGeneratorAsync(It.IsAny<AIProviderConnectionEntry>(), "embedding-model")).Returns(new ValueTask<IEmbeddingGenerator<string, Embedding<float>>>(innerGenerator.Object));

        var factory = CreateFactory(provider.Object);
        var generator = await factory.CreateEmbeddingGeneratorAsync(
            deployment,
            builder => builder.UseDefaultResilience(options => options.RateLimitRetryDelay = TimeSpan.Zero));

        var result = await generator.GenerateAsync(["hello"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(result);
        innerGenerator.Verify(generator => generator.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static DefaultAIClientFactory CreateFactory(IAIClientProvider provider)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        return new DefaultAIClientFactory(
            [provider],
            [],
            new EphemeralDataProtectionProvider(),
            services,
            Mock.Of<IAIProviderConnectionStore>());
    }

    private static HttpRequestException CreateRateLimitException()
    {
        return new HttpRequestException("Too many requests.", null, HttpStatusCode.TooManyRequests);
    }
}
