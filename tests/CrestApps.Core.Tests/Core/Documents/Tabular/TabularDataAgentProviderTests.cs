using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Services;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public class TabularDataAgentProviderTests
{
    [Fact]
    public async Task GetProfilesAsync_ReturnsAlwaysAvailableSystemToolCapableAgent()
    {
        var provider = new TabularDataAgentProvider(new StubTemplateService("You are the Tabular Data Agent."));

        var agents = await provider.GetProfilesAsync(AIProfileType.Agent, TestContext.Current.CancellationToken);

        var agent = Assert.Single(agents);
        Assert.Equal(AIProfileType.Agent, agent.Type);
        Assert.Equal(TabularDataAgentProvider.AgentName, agent.Name);
        Assert.False(string.IsNullOrWhiteSpace(agent.Description));

        Assert.True(agent.TryGet<AgentMetadata>(out var metadata));
        Assert.Equal(AgentAvailability.AlwaysAvailable, metadata.Availability);
        Assert.True(metadata.AllowToolInvocation);
        Assert.True(metadata.IsSystem);
    }

    [Fact]
    public async Task GetProfilesAsync_AgentIsAlwaysAvailableSystemAndNotUserSelectable()
    {
        var provider = new TabularDataAgentProvider(new StubTemplateService("You are the Tabular Data Agent."));

        var agents = await provider.GetProfilesAsync(AIProfileType.Agent, TestContext.Current.CancellationToken);
        var agent = Assert.Single(agents);

        Assert.True(agent.IsAlwaysAvailableAgent());
        Assert.True(agent.IsSystemAgent());

        // Always-available system agents are hidden from the user-facing agent selection list.
        Assert.False(agent.IsUserSelectableAgent());
    }

    [Fact]
    public async Task GetProfilesAsync_AgentReferencesTabularToolsAndTemplateSystemPrompt()
    {
        const string prompt = "You are the Tabular Data Agent. Use SQL.";
        var provider = new TabularDataAgentProvider(new StubTemplateService(prompt));

        var agents = await provider.GetProfilesAsync(AIProfileType.Agent, TestContext.Current.CancellationToken);
        var agent = Assert.Single(agents);

        Assert.True(agent.TryGet<FunctionInvocationMetadata>(out var functionMetadata));
        Assert.Equal(
            [
                TabularToolNames.ListTabularData,
                TabularToolNames.QueryTabularData,
                TabularToolNames.ExecuteTabularCommand,
                TabularToolNames.FillEmptyTabularCells,
                TabularToolNames.ExportTabularData,
            ],
            functionMetadata.Names);

        Assert.True(agent.TryGet<AIProfileMetadata>(out var profileMetadata));
        Assert.Equal(prompt, profileMetadata.SystemMessage);
    }

    [Fact]
    public async Task GetProfilesAsync_NonAgentType_ReturnsEmpty()
    {
        var provider = new TabularDataAgentProvider(new StubTemplateService("You are the Tabular Data Agent."));

        var profiles = await provider.GetProfilesAsync(AIProfileType.Chat, TestContext.Current.CancellationToken);

        Assert.Empty(profiles);
    }

    private sealed class StubTemplateService : ITemplateService
    {
        private readonly string _rendered;

        public StubTemplateService(string rendered)
        {
            _rendered = rendered;
        }

        public Task<IReadOnlyList<Template>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Template>>([]);

        public Task<Template> GetAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<Template>(null);

        public Task<string> RenderAsync(string id, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_rendered);

        public Task<string> MergeAsync(IEnumerable<string> ids, IDictionary<string, object> arguments = null, string separator = "\n\n", CancellationToken cancellationToken = default)
            => Task.FromResult(_rendered);
    }
}
