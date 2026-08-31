using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Moq;

namespace CrestApps.Core.Tests.Core.Realtime;

public sealed class DefaultRealtimeCapabilityResolverTests
{
    [Fact]
    public async Task IsRealtimeAvailableAsync_WhenDeploymentResolves_ReturnsTrue()
    {
        var manager = new Mock<IAIDeploymentManager>();
        manager
            .Setup(m => m.ResolveOrDefaultAsync(AIDeploymentPurpose.Realtime, "rt-deploy", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIDeployment { Name = "rt", ModelName = "gpt-realtime" });

        var available = await new DefaultRealtimeCapabilityResolver(manager.Object)
            .IsRealtimeAvailableAsync(new AIProfile { RealtimeDeploymentName = "rt-deploy" }, TestContext.Current.CancellationToken);

        Assert.True(available);
    }

    [Fact]
    public async Task IsRealtimeAvailableAsync_WhenNoDeployment_ReturnsFalse()
    {
        var manager = new Mock<IAIDeploymentManager>();
        manager
            .Setup(m => m.ResolveOrDefaultAsync(AIDeploymentPurpose.Realtime, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AIDeployment)null);

        var available = await new DefaultRealtimeCapabilityResolver(manager.Object)
            .IsRealtimeAvailableAsync(new AIProfile(), TestContext.Current.CancellationToken);

        Assert.False(available);
    }
}
