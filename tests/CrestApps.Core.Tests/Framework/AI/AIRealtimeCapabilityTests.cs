using CrestApps.Core.AI;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI;

public sealed class AIRealtimeCapabilityTests
{
    [Fact]
    public void AddCoreAIDeploymentCapabilities_RegistersRealtimeFeature()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IAIDeploymentStore>());
        services.AddCoreAIDeploymentCapabilities();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AIDeploymentCapabilityOptions>>().Value;

        Assert.True(options.Features.ContainsKey(AIDeploymentFeatureNames.Realtime));
    }
}
