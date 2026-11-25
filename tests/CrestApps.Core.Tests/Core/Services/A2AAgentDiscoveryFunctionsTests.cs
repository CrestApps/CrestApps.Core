using System.Text.Json;
using CrestApps.Core.AI.A2A.Functions;
using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.A2A.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class A2AAgentDiscoveryFunctionsTests
{
    [Fact]
    public async Task ListAvailableAgents_NoAgentRunning_ListsEveryAgent()
    {
        using var scope = AIInvocationScope.Begin();
        scope.Context.CompletionContext = new AICompletionContext();

        var result = await new ListAvailableAgentsFunction().InvokeAsync(CreateArguments(), TestContext.Current.CancellationToken);

        Assert.Equal(["research-agent", "email-agent"], ReadIds(result));
    }

    [Fact]
    public async Task ListAvailableAgents_AgentRunDirectly_LeavesItselfOut()
    {
        using var scope = AIInvocationScope.Begin();
        scope.Context.CompletionContext = new AICompletionContext { RunningAgentName = "Research-Agent" };

        var result = await new ListAvailableAgentsFunction().InvokeAsync(CreateArguments(), TestContext.Current.CancellationToken);

        Assert.Equal(["email-agent"], ReadIds(result));
    }

    [Fact]
    public async Task FindAgentForTask_AgentRunDirectly_NeverSuggestsItself()
    {
        using var scope = AIInvocationScope.Begin();
        scope.Context.CompletionContext = new AICompletionContext { RunningAgentName = "research-agent" };

        var arguments = CreateArguments();
        arguments["taskDescription"] = "research summaries";

        var result = await new FindAgentForTaskFunction().InvokeAsync(arguments, TestContext.Current.CancellationToken);

        Assert.Equal(["email-agent"], ReadIds(result));
    }

    private static AIFunctionArguments CreateArguments()
    {
        var profileManager = new Mock<IAIProfileManager>();
        profileManager
            .Setup(manager => manager.GetAsync(AIProfileType.Agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                CreateAgent("research-agent", "Researches a topic and returns research summaries."),
                CreateAgent("email-agent", "Sends research summaries by email."),
            ]);

        var connectionStore = new Mock<ICatalog<A2AConnection>>();
        connectionStore
            .Setup(store => store.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tokenizer = new Mock<ITextTokenizer>();
        tokenizer
            .Setup(t => t.Tokenize(It.IsAny<string>()))
            .Returns((string text) => text.ToLowerInvariant().Split([' ', '.'], StringSplitOptions.RemoveEmptyEntries).ToHashSet());

        var services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton(profileManager.Object)
            .AddSingleton(connectionStore.Object)
            .AddSingleton(Mock.Of<IA2AAgentCardCacheService>())
            .AddSingleton(tokenizer.Object)
            .BuildServiceProvider();

        return new AIFunctionArguments { Services = services };
    }

    private static AIProfile CreateAgent(string name, string description)
    {
        return new AIProfile
        {
            ItemId = $"{name}-id",
            Name = name,
            DisplayText = name,
            Description = description,
            Type = AIProfileType.Agent,
        };
    }

    private static string[] ReadIds(object result)
    {
        using var document = JsonDocument.Parse(Assert.IsType<string>(result));

        return [.. document.RootElement.EnumerateArray().Select(agent => agent.GetProperty("id").GetString())];
    }
}
