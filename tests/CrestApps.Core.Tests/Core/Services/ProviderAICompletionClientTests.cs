using System.Net;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class ProviderAICompletionClientTests
{
    [Fact]
    public async Task CompleteAsync_FrameworkClientUsesDefaultResilience()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"));
        var innerClient = new Mock<IChatClient>(MockBehavior.Strict);
        innerClient
            .SetupSequence(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateRateLimitException())
            .ReturnsAsync(response);

        var deployment = new AIDeployment
        {
            Name = "test-deployment",
            ClientName = TestProviderMarker.ClientName,
            ModelName = "test-model",
        };

        var clientFactory = new Mock<IAIClientFactory>(MockBehavior.Strict);
        clientFactory
            .Setup(factory => factory.CreateChatClientAsync(It.IsAny<AIDeployment>()))
            .ReturnsAsync(innerClient.Object);

        var deploymentManager = new Mock<IAIDeploymentManager>(MockBehavior.Strict);
        deploymentManager
            .Setup(manager => manager.ResolveOrDefaultAsync(AIDeploymentPurpose.Chat, It.IsAny<string>(), TestProviderMarker.ClientName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deployment);

        var completionClient = new ProviderAICompletionClient<TestProviderMarker>(
            clientFactory.Object,
            NullLoggerFactory.Instance,
            Mock.Of<IDistributedCache>(),
            new ServiceCollection().BuildServiceProvider(),
            [],
            Options.Create(new DefaultAIOptions
            {
                EnableDistributedCaching = false,
            }),
            Mock.Of<ITemplateService>(),
            deploymentManager.Object);

        var result = await completionClient.CompleteAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new AICompletionContext
            {
                ChatDeploymentName = deployment.Name,
                DisableTools = true,
            },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("done", result.Text);
        innerClient.Verify(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static HttpRequestException CreateRateLimitException()
    {
        return new HttpRequestException("Too many requests.", null, HttpStatusCode.TooManyRequests);
    }

    private sealed class TestProviderMarker : IAIClientMarker
    {
        public static string ClientName => "test-provider";
    }
}
