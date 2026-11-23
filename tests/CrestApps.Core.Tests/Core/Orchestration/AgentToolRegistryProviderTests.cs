using System.Security.Claims;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public async Task GetToolsAsync_ChatInteraction_LeavesOutAgentTheCallerMayNotUse()
    {
        var evaluator = new FakeAgentAccessEvaluator { DeniedAgentNames = { "denied-agent" } };
        var provider = CreateProvider(
            evaluator,
            CreateUser(),
            BuildOnDemandAgent("allowed-agent"),
            BuildOnDemandAgent("denied-agent"));

        using var scope = AIInvocationScope.Begin();

        var entries = await provider.GetToolsAsync(
            CreateChatInteractionContext("allowed-agent", "denied-agent"),
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(entries);
        Assert.Equal("allowed-agent", entry.Name);
        Assert.Equal(["allowed-agent", "denied-agent"], evaluator.EvaluatedAgentNames.Order());
    }

    [Fact]
    public async Task GetToolsAsync_AISession_IsNotChecked()
    {
        var evaluator = new FakeAgentAccessEvaluator { DenyAll = true };
        var provider = CreateProvider(evaluator, CreateUser(), BuildOnDemandAgent("session-agent"));

        using var scope = AIInvocationScope.Begin();

        // An AI Session has no Interaction key, so the profile is the authorization boundary.
        var entries = await provider.GetToolsAsync(
            new AICompletionContext { AgentNames = ["session-agent"] },
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(entries);
        Assert.Equal("session-agent", entry.Name);
        Assert.Empty(evaluator.EvaluatedAgentNames);
    }

    [Fact]
    public async Task GetToolsAsync_ChatInteraction_NeverChecksAlwaysAvailableOrSystemAgents()
    {
        var evaluator = new FakeAgentAccessEvaluator { DenyAll = true };
        var provider = CreateProvider(
            evaluator,
            CreateUser(),
            BuildAlwaysAvailableAgent(),
            BuildOnDemandAgent("system-agent", isSystem: true));

        using var scope = AIInvocationScope.Begin();

        var entries = await provider.GetToolsAsync(
            CreateChatInteractionContext("system-agent"),
            TestContext.Current.CancellationToken);

        Assert.Equal(["always-agent", "system-agent"], entries.Select(entry => entry.Name).Order());
        Assert.Empty(evaluator.EvaluatedAgentNames);
    }

    [Fact]
    public async Task GetToolsAsync_ChatInteractionWithoutCaller_IsNotChecked()
    {
        var evaluator = new FakeAgentAccessEvaluator { DenyAll = true };

        // A null principal means a trusted server-side call, such as a background task.
        var provider = CreateProvider(evaluator, user: null, BuildOnDemandAgent("background-agent"));

        using var scope = AIInvocationScope.Begin();

        var entries = await provider.GetToolsAsync(
            CreateChatInteractionContext("background-agent"),
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(entries);
        Assert.Equal("background-agent", entry.Name);
        Assert.Empty(evaluator.EvaluatedAgentNames);
    }

    [Fact]
    public async Task DefaultAIAgentAccessEvaluator_AllowsEveryAgent()
    {
        var evaluator = new DefaultAIAgentAccessEvaluator();

        Assert.True(await evaluator.IsAuthorizedAsync(CreateUser(), BuildOnDemandAgent("any-agent")));
        Assert.True(await evaluator.IsAuthorizedAsync(null, BuildOnDemandAgent("any-agent")));
    }

    private static AgentToolRegistryProvider CreateProvider(params AIProfile[] agents)
    {
        return CreateProvider(new FakeAgentAccessEvaluator(), CreateUser(), agents);
    }

    private static AgentToolRegistryProvider CreateProvider(
        IAIAgentAccessEvaluator evaluator,
        ClaimsPrincipal user,
        params AIProfile[] agents)
    {
        var manager = new Mock<IAIProfileManager>();
        manager
            .Setup(m => m.GetAsync(AIProfileType.Agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agents);

        var userAccessor = new Mock<IUserAccessor>();
        userAccessor.SetupGet(accessor => accessor.User).Returns(user);

        return new AgentToolRegistryProvider(
            manager.Object,
            evaluator,
            userAccessor.Object,
            NullLogger<AgentToolRegistryProvider>.Instance);
    }

    private static AICompletionContext CreateChatInteractionContext(params string[] agentNames)
    {
        var context = new AICompletionContext { AgentNames = agentNames };
        context.AdditionalProperties[AICompletionContextKeys.Interaction] = new ChatInteraction { ItemId = "interaction-1" };

        return context;
    }

    private static ClaimsPrincipal CreateUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "user")], "Test"));
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

    private static AIProfile BuildOnDemandAgent(string name, bool isSystem = false)
    {
        var agent = new AIProfile
        {
            ItemId = $"{name}-id",
            Name = name,
            Description = $"The {name} test agent.",
            Type = AIProfileType.Agent,
        };

        agent.Put(new AgentMetadata { Availability = AgentAvailability.OnDemand, IsSystem = isSystem });

        return agent;
    }

    private sealed class FakeAgentAccessEvaluator : IAIAgentAccessEvaluator
    {
        public bool DenyAll { get; init; }

        public HashSet<string> DeniedAgentNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> EvaluatedAgentNames { get; } = [];

        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, AIProfile agent)
        {
            EvaluatedAgentNames.Add(agent.Name);

            return Task.FromResult(!DenyAll && !DeniedAgentNames.Contains(agent.Name));
        }
    }
}
