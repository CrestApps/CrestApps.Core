using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public class TabularDataAgentProviderTests
{
    [Fact]
    public async Task GetAgentsAsync_ReturnsAlwaysAvailableBuiltInToolCapableAgent()
    {
        var provider = new TabularDataAgentProvider();

        var agents = await provider.GetAgentsAsync(TestContext.Current.CancellationToken);

        var agent = Assert.Single(agents);
        Assert.Equal(AIProfileType.Agent, agent.Type);
        Assert.Equal(TabularDataAgentProvider.AgentName, agent.Name);
        Assert.False(string.IsNullOrWhiteSpace(agent.Description));

        Assert.True(agent.TryGet<AgentMetadata>(out var metadata));
        Assert.Equal(AgentAvailability.AlwaysAvailable, metadata.Availability);
        Assert.True(metadata.AllowToolInvocation);
        Assert.True(metadata.IsBuiltIn);
    }

    [Fact]
    public async Task GetAgentsAsync_AgentIsAlwaysAvailableBuiltInAndNotUserSelectable()
    {
        var provider = new TabularDataAgentProvider();

        var agents = await provider.GetAgentsAsync(TestContext.Current.CancellationToken);
        var agent = Assert.Single(agents);

        Assert.True(agent.IsAlwaysAvailableAgent());
        Assert.True(agent.IsBuiltInAgent());

        // Always-available system agents are hidden from the user-facing agent selection list.
        Assert.False(agent.IsUserSelectableAgent());
    }

    [Fact]
    public async Task GetAgentsAsync_AgentReferencesTabularToolsAndSystemPrompt()
    {
        var provider = new TabularDataAgentProvider();

        var agents = await provider.GetAgentsAsync(TestContext.Current.CancellationToken);
        var agent = Assert.Single(agents);

        Assert.True(agent.TryGet<FunctionInvocationMetadata>(out var functionMetadata));
        Assert.Equal(
            [
                TabularToolNames.ListTabularData,
                TabularToolNames.QueryTabularData,
                TabularToolNames.ExecuteTabularCommand,
            ],
            functionMetadata.Names);

        Assert.True(agent.TryGet<AIProfileMetadata>(out var profileMetadata));
        Assert.False(string.IsNullOrWhiteSpace(profileMetadata.SystemMessage));
    }
}
