using CrestApps.Core.AI;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class AgentOrchestrationContextBuilderHandlerTests
{
    [Fact]
    public async Task BuiltAsync_AlwaysAvailableAgent_IsListedInSystemMessage()
    {
        var templateService = CreateTemplateService();
        var handler = CreateHandler(templateService.Object, BuildAlwaysAvailableAgent("always-agent"));
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext(),
        };

        using var scope = AIInvocationScope.Begin();

        await handler.BuiltAsync(new OrchestrationContextBuiltContext(new AIProfile(), context), TestContext.Current.CancellationToken);

        Assert.Contains("[Available Agents]", context.SystemMessageBuilder.ToString());
    }

    [Fact]
    public async Task BuiltAsync_OnlyAgentIsTheRunningAgent_AddsNoAgentList()
    {
        var templateService = CreateTemplateService();
        var handler = CreateHandler(templateService.Object, BuildAlwaysAvailableAgent("always-agent"));
        var context = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext
            {
                AgentNames = ["always-agent"],
                RunningAgentName = "always-agent",
            },
        };

        using var scope = AIInvocationScope.Begin();

        await handler.BuiltAsync(new OrchestrationContextBuiltContext(new AIProfile(), context), TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, context.SystemMessageBuilder.ToString());
        templateService.Verify(service => service.RenderAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    private static Mock<ITemplateService> CreateTemplateService()
    {
        var templateService = new Mock<ITemplateService>();
        templateService
            .Setup(service => service.RenderAsync(AITemplateIds.AgentAvailability, It.IsAny<IDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("[Available Agents]");

        return templateService;
    }

    private static AgentOrchestrationContextBuilderHandler CreateHandler(ITemplateService templateService, params AIProfile[] agents)
    {
        var manager = new Mock<IAIProfileManager>();
        manager
            .Setup(m => m.GetAsync(AIProfileType.Agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agents);

        return new AgentOrchestrationContextBuilderHandler(manager.Object, templateService, NullLogger<AgentOrchestrationContextBuilderHandler>.Instance);
    }

    private static AIProfile BuildAlwaysAvailableAgent(string name)
    {
        var agent = new AIProfile
        {
            ItemId = $"{name}-id",
            Name = name,
            Description = $"The {name} test agent.",
            Type = AIProfileType.Agent,
        };

        agent.Put(new AgentMetadata { Availability = AgentAvailability.AlwaysAvailable });

        return agent;
    }
}
