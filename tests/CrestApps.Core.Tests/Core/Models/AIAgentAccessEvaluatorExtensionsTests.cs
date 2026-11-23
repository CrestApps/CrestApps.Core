using System.Security.Claims;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;

namespace CrestApps.Core.Tests.Core.Models;

public sealed class AIAgentAccessEvaluatorExtensionsTests
{
    [Fact]
    public async Task GetAuthorizedSelectableAgents_LeavesOutDeniedAndNonSelectableAgents()
    {
        var evaluator = new FakeAgentAccessEvaluator { DeniedAgentNames = { "denied-agent" } };

        var authorized = await evaluator.GetAuthorizedSelectableAgentsAsync(
            [
                BuildAgent("allowed-agent"),
                BuildAgent("denied-agent"),
                BuildAgent("system-agent", isSystem: true),
                BuildAgent("always-agent", availability: AgentAvailability.AlwaysAvailable),
            ],
            CreateUser(),
            TestContext.Current.CancellationToken);

        // Only the selectable, allowed agent is kept. System and always-available agents never reach the evaluator.
        Assert.Equal(["allowed-agent"], authorized.Select(agent => agent.Name));
        Assert.Equal(["allowed-agent", "denied-agent"], evaluator.EvaluatedAgentNames.Order());
    }

    [Fact]
    public async Task GetAuthorizedSelectableAgentNames_ReturnsOnlyAllowedNames()
    {
        var evaluator = new FakeAgentAccessEvaluator { DeniedAgentNames = { "denied-agent" } };

        var names = await evaluator.GetAuthorizedSelectableAgentNamesAsync(
            [BuildAgent("allowed-agent"), BuildAgent("denied-agent")],
            CreateUser(),
            TestContext.Current.CancellationToken);

        Assert.Equal(["allowed-agent"], names);
        Assert.Contains("ALLOWED-AGENT", names);
    }

    [Fact]
    public async Task GetAuthorizedSelectableAgents_WithNoAgents_ReturnsEmpty()
    {
        var evaluator = new FakeAgentAccessEvaluator();

        var authorized = await evaluator.GetAuthorizedSelectableAgentsAsync(null, CreateUser(), TestContext.Current.CancellationToken);

        Assert.Empty(authorized);
    }

    private static ClaimsPrincipal CreateUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "user")], "Test"));
    }

    private static AIProfile BuildAgent(
        string name,
        bool isSystem = false,
        AgentAvailability availability = AgentAvailability.OnDemand)
    {
        var agent = new AIProfile
        {
            ItemId = $"{name}-id",
            Name = name,
            Description = $"The {name} test agent.",
            Type = AIProfileType.Agent,
        };

        agent.Put(new AgentMetadata { Availability = availability, IsSystem = isSystem });

        return agent;
    }

    private sealed class FakeAgentAccessEvaluator : IAIAgentAccessEvaluator
    {
        public HashSet<string> DeniedAgentNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> EvaluatedAgentNames { get; } = [];

        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, AIProfile agent)
        {
            EvaluatedAgentNames.Add(agent.Name);

            return Task.FromResult(!DeniedAgentNames.Contains(agent.Name));
        }
    }
}
