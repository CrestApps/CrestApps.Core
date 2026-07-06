using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using Moq;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class AgentToolRegistryProviderTests
{
    [Fact]
    public async Task GetToolsAsync_AlwaysAvailableAgent_IsExposedAsTool()
    {
        var provider = CreateProvider(BuildAlwaysAvailableAgent());

        using var scope = AIInvocationScope.Begin();

        var entries = await provider.GetToolsAsync(new AICompletionContext(), TestContext.Current.CancellationToken);

        var entry = Assert.Single(entries);
        Assert.Equal("always-agent", entry.Name);
        Assert.Equal(ToolRegistryEntrySource.Agent, entry.Source);
    }

    [Fact]
    public async Task GetToolsAsync_InsideSubAgent_SuppressesAgents()
    {
        var provider = CreateProvider(BuildAlwaysAvailableAgent());

        using var scope = AIInvocationScope.Begin();

        // Simulate running inside a tool-capable sub-agent so agents must not be exposed as tools
        // (prevents agent-to-agent recursion).
        scope.Context.AgentInvocationDepth = 1;

        var entries = await provider.GetToolsAsync(new AICompletionContext(), TestContext.Current.CancellationToken);

        Assert.Empty(entries);
    }

    private static AgentToolRegistryProvider CreateProvider(params AIProfile[] agents)
    {
        var manager = new Mock<IAIProfileManager>();
        manager
            .Setup(m => m.GetAsync(AIProfileType.Agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agents);

        return new AgentToolRegistryProvider(manager.Object);
    }

    private static AIProfile BuildAlwaysAvailableAgent()
    {
        var agent = new AIProfile
        {
            ItemId = "agent-1",
            Name = "always-agent",
            Description = "An always-available test agent.",
            Type = AIProfileType.Agent,
        };

        agent.Put(new AgentMetadata { Availability = AgentAvailability.AlwaysAvailable });

        return agent;
    }
}
