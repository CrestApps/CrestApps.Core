using CrestApps.Core.AI;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Handlers;

public sealed class AgentOrchestrationContextBuilderHandlerTests
{
    [Fact]
    public void AddCoreAIOrchestration_RegistersTheAgentHandler()
    {
        // The handler existed without a registration, so no host wrote the available agents into the system message.
        var services = new ServiceCollection();

        services.AddCoreAIOrchestration();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IOrchestrationContextBuilderHandler) &&
            descriptor.ImplementationType == typeof(AgentOrchestrationContextBuilderHandler) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task BuiltAsync_RequestedOnDemandAgent_DescribesItInTheSystemMessage()
    {
        using var serviceProvider = CreateServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var context = CreateContext(agentNames: ["analyst"]);

        await CreateHandler(scope.ServiceProvider, CreateAgent("analyst", AgentAvailability.OnDemand)).BuiltAsync(context, TestContext.Current.CancellationToken);

        var systemMessage = context.OrchestrationContext.SystemMessageBuilder.ToString();
        Assert.Contains("[Available Agents]", systemMessage, StringComparison.Ordinal);
        Assert.Contains("**analyst**: Counts the calls.", systemMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuiltAsync_OnDemandAgentThatIsNotRequested_AddsNothing()
    {
        using var serviceProvider = CreateServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var context = CreateContext(agentNames: ["writer"]);

        await CreateHandler(scope.ServiceProvider, CreateAgent("analyst", AgentAvailability.OnDemand)).BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(0, context.OrchestrationContext.SystemMessageBuilder.Length);
    }

    [Fact]
    public async Task BuiltAsync_AlwaysAvailableAgent_DescribesItWithoutARequest()
    {
        using var serviceProvider = CreateServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var context = CreateContext(agentNames: null);

        await CreateHandler(scope.ServiceProvider, CreateAgent("helper", AgentAvailability.AlwaysAvailable)).BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.Contains("**helper**", context.OrchestrationContext.SystemMessageBuilder.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuiltAsync_InsideASubAgent_AddsNothing()
    {
        using var serviceProvider = CreateServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var context = CreateContext(agentNames: null);
        using var invocation = AIInvocationScope.Begin();
        AIInvocationScope.Current.AgentInvocationDepth++;

        await CreateHandler(scope.ServiceProvider, CreateAgent("helper", AgentAvailability.AlwaysAvailable)).BuiltAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(0, context.OrchestrationContext.SystemMessageBuilder.Length);
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreAIServices();
        services.AddCoreAIOrchestration();

        return services.BuildServiceProvider();
    }

    private static AgentOrchestrationContextBuilderHandler CreateHandler(IServiceProvider services, params AIProfile[] agents)
    {
        var profileManager = new Mock<IAIProfileManager>();
        profileManager
            .Setup(manager => manager.GetAsync(AIProfileType.Agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agents);

        return new AgentOrchestrationContextBuilderHandler(
            profileManager.Object,
            services.GetRequiredService<ITemplateService>(),
            NullLogger<AgentOrchestrationContextBuilderHandler>.Instance);
    }

    private static OrchestrationContextBuiltContext CreateContext(string[] agentNames)
    {
        var orchestrationContext = new OrchestrationContext
        {
            CompletionContext = new AICompletionContext { AgentNames = agentNames },
        };

        return new OrchestrationContextBuiltContext(new ChatInteraction(), orchestrationContext);
    }

    private static AIProfile CreateAgent(string name, AgentAvailability availability)
    {
        var agent = new AIProfile
        {
            Name = name,
            Type = AIProfileType.Agent,
            Description = "Counts the calls.",
        };

        agent.Put(new AgentMetadata { Availability = availability });

        return agent;
    }
}
