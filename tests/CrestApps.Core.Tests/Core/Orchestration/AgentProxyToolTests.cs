using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class AgentProxyToolTests
{
    [Theory]
    [InlineData(null, "caller-model")]
    [InlineData("agent-model", "agent-model")]
    public async Task ToolAgent_RunsOnTheCallersDeployment_UnlessItNamesItsOwn(string agentDeployment, string expected)
    {
        var agent = new AIProfile { Name = "pdf-agent", Type = AIProfileType.Agent, ChatDeploymentName = agentDeployment };
        agent.Put(new AgentMetadata { AllowToolInvocation = true });

        var profiles = new Mock<IAIProfileManager>();
        profiles.Setup(manager => manager.GetAsync(AIProfileType.Agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync([agent]);

        OrchestrationContext built = null;
        var contexts = new Mock<IOrchestrationContextBuilder>();
        contexts.Setup(builder => builder.BuildAsync(agent, It.IsAny<Action<OrchestrationContext>>(), It.IsAny<CancellationToken>()))
            .Returns((object _, Action<OrchestrationContext> configure, CancellationToken _) =>
            {
                // The profile handler has already copied the agent's own deployment, if any.
                built = new OrchestrationContext { CompletionContext = new AICompletionContext { ChatDeploymentName = agentDeployment } };
                configure(built);

                return ValueTask.FromResult(built);
            });

        var orchestrator = new Mock<IOrchestrator>();
        orchestrator.Setup(candidate => candidate.ExecuteStreamingAsync(It.IsAny<OrchestrationContext>(), It.IsAny<CancellationToken>()))
            .Returns(Reply("done"));

        var resolver = new Mock<IOrchestratorResolver>();
        resolver.Setup(candidate => candidate.Resolve(It.IsAny<string>())).Returns(orchestrator.Object);

        var services = new ServiceCollection()
            .AddSingleton(profiles.Object)
            .AddSingleton(contexts.Object)
            .AddSingleton(resolver.Object)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .BuildServiceProvider();

        using var scope = AIInvocationScope.Begin();
        scope.Context.CompletionContext = new AICompletionContext { ChatDeploymentName = "caller-model" };

        var tool = new AgentProxyTool("pdf-agent", "Makes PDFs.");
        var result = await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object> { ["prompt"] = "make a PDF" }) { Services = services }, TestContext.Current.CancellationToken);

        Assert.Equal("done", result?.ToString());
        Assert.Equal(expected, built.CompletionContext.ChatDeploymentName);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Reply(string text)
    {
        await Task.Yield();

        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
    }
}
