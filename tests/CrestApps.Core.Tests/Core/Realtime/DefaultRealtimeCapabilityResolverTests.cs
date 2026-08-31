using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Realtime;

public sealed class DefaultRealtimeCapabilityResolverTests
{
    private static AIDeploymentCapabilities RealtimeCapabilities()
        => new([new AIModelFeatureDescriptor { Name = AIModelFeatureNames.Realtime }], []);

    private static DefaultRealtimeCapabilityResolver CreateResolver(
        Mock<IAIDeploymentManager> manager,
        Mock<IAIModelCapabilityService> capabilityService,
        string defaultRealtimeDeploymentName = null)
    {
        var settings = new Mock<IOptionsMonitor<DefaultAIDeploymentSettings>>();
        settings
            .Setup(s => s.CurrentValue)
            .Returns(new DefaultAIDeploymentSettings { DefaultRealtimeDeploymentName = defaultRealtimeDeploymentName });

        return new DefaultRealtimeCapabilityResolver(manager.Object, capabilityService.Object, settings.Object);
    }

    [Fact]
    public async Task IsRealtimeAvailableAsync_WhenNamedDeploymentDeclaresRealtime_ReturnsTrue()
    {
        var deployment = new AIDeployment { Name = "rt", ModelName = "gpt-realtime" };

        var manager = new Mock<IAIDeploymentManager>();
        manager
            .Setup(m => m.FindByNameAsync("rt-deploy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(deployment);

        var capabilityService = new Mock<IAIModelCapabilityService>();
        capabilityService.Setup(c => c.GetCapabilities(deployment)).Returns(RealtimeCapabilities());

        var available = await CreateResolver(manager, capabilityService)
            .IsRealtimeAvailableAsync(new AIProfile { RealtimeDeploymentName = "rt-deploy" }, TestContext.Current.CancellationToken);

        Assert.True(available);
    }

    [Fact]
    public async Task IsRealtimeAvailableAsync_WhenNamedDeploymentDoesNotDeclareRealtime_ReturnsFalse()
    {
        var deployment = new AIDeployment { Name = "rt", ModelName = "gpt-4o" };

        var manager = new Mock<IAIDeploymentManager>();
        manager
            .Setup(m => m.FindByNameAsync("rt-deploy", It.IsAny<CancellationToken>()))
            .ReturnsAsync(deployment);

        var capabilityService = new Mock<IAIModelCapabilityService>();
        capabilityService.Setup(c => c.GetCapabilities(deployment)).Returns(AIDeploymentCapabilities.Empty);

        var available = await CreateResolver(manager, capabilityService)
            .IsRealtimeAvailableAsync(new AIProfile { RealtimeDeploymentName = "rt-deploy" }, TestContext.Current.CancellationToken);

        Assert.False(available);
    }

    [Fact]
    public async Task IsRealtimeAvailableAsync_WhenNoDeployment_ReturnsFalse()
    {
        var manager = new Mock<IAIDeploymentManager>();
        manager
            .Setup(m => m.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AIDeployment)null);
        manager
            .Setup(m => m.GetByPurposeAsync(AIDeploymentPurpose.Chat, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var capabilityService = new Mock<IAIModelCapabilityService>();

        var available = await CreateResolver(manager, capabilityService)
            .IsRealtimeAvailableAsync(new AIProfile(), TestContext.Current.CancellationToken);

        Assert.False(available);
    }

    [Fact]
    public async Task ResolveRealtimeDeploymentAsync_WhenNoName_FallsBackToRealtimeCapableChatDeployment()
    {
        var realtime = new AIDeployment { Name = "rt", ModelName = "gpt-realtime" };
        var plain = new AIDeployment { Name = "chat", ModelName = "gpt-4o" };

        var manager = new Mock<IAIDeploymentManager>();
        manager
            .Setup(m => m.GetByPurposeAsync(AIDeploymentPurpose.Chat, It.IsAny<CancellationToken>()))
            .ReturnsAsync([plain, realtime]);

        var capabilityService = new Mock<IAIModelCapabilityService>();
        capabilityService.Setup(c => c.GetCapabilities(plain)).Returns(AIDeploymentCapabilities.Empty);
        capabilityService.Setup(c => c.GetCapabilities(realtime)).Returns(RealtimeCapabilities());

        var resolved = await CreateResolver(manager, capabilityService)
            .ResolveRealtimeDeploymentAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(realtime, resolved);
    }

    [Fact]
    public async Task GetRealtimeDeploymentsAsync_ReturnsOnlyRealtimeCapableChatDeployments()
    {
        var realtime = new AIDeployment { Name = "rt", ModelName = "gpt-realtime" };
        var plain = new AIDeployment { Name = "chat", ModelName = "gpt-4o" };

        var manager = new Mock<IAIDeploymentManager>();
        manager
            .Setup(m => m.GetByPurposeAsync(AIDeploymentPurpose.Chat, It.IsAny<CancellationToken>()))
            .ReturnsAsync([plain, realtime]);

        var capabilityService = new Mock<IAIModelCapabilityService>();
        capabilityService.Setup(c => c.GetCapabilities(plain)).Returns(AIDeploymentCapabilities.Empty);
        capabilityService.Setup(c => c.GetCapabilities(realtime)).Returns(RealtimeCapabilities());

        var deployments = await CreateResolver(manager, capabilityService)
            .GetRealtimeDeploymentsAsync(TestContext.Current.CancellationToken);

        Assert.Single(deployments);
        Assert.Same(realtime, deployments[0]);
    }
}
