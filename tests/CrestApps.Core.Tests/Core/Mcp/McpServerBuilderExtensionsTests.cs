using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using ServerToolOptions = CrestApps.Core.AI.Mcp.Models.McpServerOptions;

namespace CrestApps.Core.Tests.Core.Mcp;

public sealed class McpServerBuilderExtensionsTests
{
    /// <summary>
    /// Verifies that with the default settings (nothing allowed and <c>ExposeAllTools</c> off) no tools
    /// are listed, even when tools are registered, so an MCP server exposes nothing until explicitly told to.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_DefaultDeny_ReturnsEmpty()
    {
        var services = CreateServices();

        AddLocalTool(services, "search-key", new TestAIFunction("search"));
        AddLocalTool(services, "create-key", new TestAIFunction("create"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Tools);
    }

    /// <summary>
    /// Verifies that enabling <c>ExposeAllTools</c> lists every selectable tool while hidden and system
    /// tools are omitted, because system tools are auto-included by agents and must not be exposed over MCP.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_ExposeAllTools_ReturnsVisibleToolsAndOmitsHidden()
    {
        var services = CreateServices(configureOptions: options => options.ExposeAllTools = true);

        AddLocalTool(services, "search-key", new TestAIFunction("search"));
        AddLocalTool(services, "hidden-key", new TestAIFunction("hidden"), hidden: true);
        AddLocalTool(services, "system-key", new TestAIFunction("system"), isSystemTool: true);
        AddLocalTool(services, "create-key", new TestAIFunction("create"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["search", "create"], result.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// Verifies that the allow-list exposes only the tools whose registration key is listed.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_AllowList_ExposesOnlyNamedTools()
    {
        var services = CreateServices(configureOptions: options => options.Tools = ["search-key"]);

        AddLocalTool(services, "search-key", new TestAIFunction("search"));
        AddLocalTool(services, "create-key", new TestAIFunction("create"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["search"], result.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// Verifies that allow-list matching is case-insensitive.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_AllowList_MatchesNameCaseInsensitively()
    {
        var services = CreateServices(configureOptions: options => options.Tools = ["SEARCH-KEY"]);

        AddLocalTool(services, "search-key", new TestAIFunction("search"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["search"], result.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// Verifies that keyed tool creation failures are logged and skipped instead of failing the whole list.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_LogsAndSkipsKeyedServiceCreationFailures()
    {
        var services = CreateServices(configureOptions: options => options.ExposeAllTools = true);
        var logger = new Mock<ILogger<IMcpServerPromptService>>();
        var failure = new InvalidOperationException("Tool creation failed.");

        AddLocalToolDefinition(services, "broken-key");
        services.AddKeyedSingleton<AITool>("broken-key", (_, _) => throw failure);
        AddLocalTool(services, "healthy-key", new TestAIFunction("healthy"));
        services.AddSingleton(logger.Object);

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["healthy"], result.Tools.Select(tool => tool.Name));
#pragma warning disable CA1873
        logger.Verify(
            value => value.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString().Contains(
                        "Error creating tool instance for 'broken-key'.",
                        StringComparison.Ordinal)),
                It.Is<Exception>(exception => ReferenceEquals(exception, failure)),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
#pragma warning restore CA1873
    }

    /// <summary>
    /// Verifies that a configured tool instance is exposed when its name is on the allow-list.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_AllowList_ExposesNamedToolInstance()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-1",
            Source = "docs-source",
            Name = "crestapps-docs",
        };

        var services = CreateServices(configureOptions: options => options.Tools = ["crestapps-docs"]);

        AddToolInstances(services, instance);
        AddToolInstanceSource(services, "docs-source");

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["crestapps-docs"], result.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// Verifies that configured tool instances are exposed when <c>ExposeAllTools</c> is enabled.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_ExposeAll_IncludesToolInstances()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-1",
            Source = "docs-source",
            Name = "crestapps-docs",
        };

        var services = CreateServices(configureOptions: options => options.ExposeAllTools = true);

        AddToolInstances(services, instance);
        AddToolInstanceSource(services, "docs-source");

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(result.Tools, tool => tool.Name == "crestapps-docs");
    }

    /// <summary>
    /// Verifies that configured tool instances are not exposed by default.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_DefaultDeny_OmitsToolInstances()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-1",
            Source = "docs-source",
            Name = "crestapps-docs",
        };

        var services = CreateServices();

        AddToolInstances(services, instance);
        AddToolInstanceSource(services, "docs-source");

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Tools);
    }

    /// <summary>
    /// Verifies that a tool not on the allow-list cannot be invoked through the call handler.
    /// </summary>
    [Fact]
    public async Task CallToolHandler_DefaultDeny_RejectsTool()
    {
        var services = CreateServices();

        AddLocalTool(services, "search", new TestAIFunction("search"));

        using var serviceProvider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<McpException>(async () =>
            await InvokeCallToolHandlerAsync(
                serviceProvider,
                "search",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that an allow-listed tool can be invoked through the call handler.
    /// </summary>
    [Fact]
    public async Task CallToolHandler_InvokesAllowedTool()
    {
        var services = CreateServices(configureOptions: options => options.Tools = ["search"]);

        AddLocalTool(services, "search", new TestAIFunction("search"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeCallToolHandlerAsync(
            serviceProvider,
            "search",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
    }

    /// <summary>
    /// Verifies that a tool advertised by its function name can be invoked even when the function name
    /// differs from the registration key it is keyed under, mirroring how the list handler publishes the
    /// function name rather than the key.
    /// </summary>
    [Fact]
    public async Task CallToolHandler_InvokesTool_WhenFunctionNameDiffersFromKey()
    {
        var services = CreateServices(configureOptions: options => options.Tools = ["search-key"]);

        AddLocalTool(services, "search-key", new TestAIFunction("search"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeCallToolHandlerAsync(
            serviceProvider,
            "search",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
    }
    [Fact]
    public async Task CallToolHandler_ExposeAll_InvokesTool()
    {
        var services = CreateServices(configureOptions: options => options.ExposeAllTools = true);

        AddLocalTool(services, "search", new TestAIFunction("search"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeCallToolHandlerAsync(
            serviceProvider,
            "search",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
    }

    /// <summary>
    /// Verifies that an allow-listed tool instance can be invoked through the call handler by its name.
    /// </summary>
    [Fact]
    public async Task CallToolHandler_InvokesAllowedToolInstance()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-1",
            Source = "docs-source",
            Name = "crestapps-docs",
        };

        var services = CreateServices(configureOptions: options => options.Tools = ["crestapps-docs"]);

        AddToolInstances(services, instance);
        AddToolInstanceSource(services, "docs-source");

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeCallToolHandlerAsync(
            serviceProvider,
            "crestapps-docs",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
    }

    /// <summary>
    /// Verifies that a tool instance not on the allow-list cannot be invoked through the call handler.
    /// </summary>
    [Fact]
    public async Task CallToolHandler_DefaultDeny_RejectsToolInstance()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-1",
            Source = "docs-source",
            Name = "crestapps-docs",
        };

        var services = CreateServices();

        AddToolInstances(services, instance);
        AddToolInstanceSource(services, "docs-source");

        using var serviceProvider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<McpException>(async () =>
            await InvokeCallToolHandlerAsync(
                serviceProvider,
                "crestapps-docs",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that a system tool cannot be invoked through the call handler even when
    /// <c>ExposeAllTools</c> is enabled, because system tools are never exposed over MCP.
    /// </summary>
    [Fact]
    public async Task CallToolHandler_ExposeAll_RejectsSystemTool()
    {
        var services = CreateServices(configureOptions: options => options.ExposeAllTools = true);

        AddLocalTool(services, "system", new TestAIFunction("system"), isSystemTool: true);

        using var serviceProvider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<McpException>(async () =>
            await InvokeCallToolHandlerAsync(
                serviceProvider,
                "system",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that agents stay hidden until explicitly allowed, so adding the feature never widens an
    /// existing server's surface on upgrade.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_DefaultDeny_DoesNotExposeAgents()
    {
        var services = CreateServices();

        AddAgents(services, CreateAgent("researcher"), CreateAgent("summarizer"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Tools);
    }

    /// <summary>
    /// Verifies that turning on every tool does NOT turn on every agent. An agent runs a whole profile with
    /// its own tools and credentials, so a server that had opted into exposing tools must not start exposing
    /// agents the moment this feature ships.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_ExposeAllTools_DoesNotExposeAgents()
    {
        var services = CreateServices(configureOptions: options => options.ExposeAllTools = true);

        AddLocalTool(services, "search-key", new TestAIFunction("search"));
        AddAgents(services, CreateAgent("researcher"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["search"], result.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// Verifies that an allow-listed agent is exposed as a tool carrying the agent's own description and the
    /// prompt schema, so a client calls it the way the orchestrator does.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_AgentAllowList_ExposesOnlyNamedAgents()
    {
        var services = CreateServices(configureOptions: options => options.Agents = ["researcher"]);

        AddAgents(services, CreateAgent("researcher"), CreateAgent("summarizer"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        var tool = Assert.Single(result.Tools);

        Assert.Equal("researcher", tool.Name);
        Assert.Equal("The researcher agent.", tool.Description);
        Assert.True(tool.InputSchema.TryGetProperty("properties", out var properties));
        Assert.True(properties.TryGetProperty("prompt", out _));
    }

    /// <summary>
    /// Verifies that <c>ExposeAllAgents</c> lists every agent, and that an agent missing a description is
    /// skipped because a client would have no way to tell what it is for.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_ExposeAllAgents_ListsEveryDescribedAgent()
    {
        var services = CreateServices(configureOptions: options => options.ExposeAllAgents = true);

        AddAgents(
            services,
            CreateAgent("researcher"),
            CreateAgent("summarizer"),
            new AIProfile { ItemId = "3", Name = "undescribed", Type = AIProfileType.Agent });

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["researcher", "summarizer"], result.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// Verifies that an agent sharing a name with an exposed tool is dropped from the listing, so the listed
    /// name matches what the call handler — which resolves tools first — will actually invoke.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_AgentNameCollidingWithTool_KeepsTheTool()
    {
        var services = CreateServices(configureOptions: options =>
        {
            options.ExposeAllTools = true;
            options.ExposeAllAgents = true;
        });

        AddLocalTool(services, "researcher-key", new TestAIFunction("researcher", "The registered tool."));
        AddAgents(services, CreateAgent("researcher"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        var tool = Assert.Single(result.Tools);

        Assert.Equal("researcher", tool.Name);
        Assert.Equal("The registered tool.", tool.Description);
    }

    /// <summary>
    /// Verifies that availability is irrelevant to MCP exposure. <c>AlwaysAvailable</c> versus
    /// <c>OnDemand</c> governs a completion's token budget, not who may reach an agent from outside, so the
    /// allow-list stays the only gate.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_AgentAvailability_DoesNotAffectExposure()
    {
        var onDemand = CreateAgent("on-demand");
        var alwaysAvailable = CreateAgent("always-available");
        alwaysAvailable.Put(new AgentMetadata { Availability = AgentAvailability.AlwaysAvailable });

        var services = CreateServices(configureOptions: options => options.Agents = ["on-demand"]);

        AddAgents(services, onDemand, alwaysAvailable);

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["on-demand"], result.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// Verifies that an agent that was never allow-listed cannot be invoked by name, so the listing is the
    /// real boundary rather than merely a discovery hint.
    /// </summary>
    [Fact]
    public async Task CallToolHandler_UnlistedAgent_Throws()
    {
        var services = CreateServices();

        AddAgents(services, CreateAgent("researcher"));

        using var serviceProvider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<McpException>(async () =>
            await InvokeCallToolHandlerAsync(
                serviceProvider,
                "researcher",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that the call handler establishes an AI invocation scope. Without one, an agent reached over
    /// MCP silently runs with its tools disabled, because the proxy treats an untrackable recursion depth as
    /// unsafe — a wrong answer rather than an error.
    /// </summary>
    [Fact]
    public async Task CallToolHandler_EstablishesAnInvocationScope()
    {
        var services = CreateServices(configureOptions: options => options.ExposeAllTools = true);
        var probe = new ScopeProbeFunction("probe");

        AddLocalTool(services, "probe-key", probe);

        using var serviceProvider = services.BuildServiceProvider();

        Assert.Null(AIInvocationScope.Current);

        await InvokeCallToolHandlerAsync(
            serviceProvider,
            "probe",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(probe.SawScope);

        // The scope is the call's own; it must not leak into whatever runs next on this flow.
        Assert.Null(AIInvocationScope.Current);
    }

    /// <summary>
    /// Verifies that the scope an MCP call establishes starts at depth zero, which is what lets an agent run
    /// its own configured tools rather than falling back to the tool-less path.
    /// </summary>
    [Fact]
    public async Task CallToolHandler_InvocationScopeStartsAtTopLevelDepth()
    {
        var services = CreateServices(configureOptions: options => options.ExposeAllTools = true);
        var probe = new ScopeProbeFunction("probe");

        AddLocalTool(services, "probe-key", probe);

        using var serviceProvider = services.BuildServiceProvider();

        await InvokeCallToolHandlerAsync(
            serviceProvider,
            "probe",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, probe.ObservedAgentDepth);
    }

    /// <summary>
    /// Verifies the central contract of exposing an agent: the allow-list decides what a client may INVOKE,
    /// never what an agent uses internally. An allow-listed agent runs through the orchestrator with its own
    /// configured tools even though none of those tools is exposed over MCP — and those tools stay
    /// unreachable by name, so exposing the agent never exposes its internals.
    /// </summary>
    [Fact]
    public async Task CallToolHandler_AllowedAgent_RunsItsOwnToolsWithoutExposingThem()
    {
        var agent = CreateAgent("researcher");
        agent.Put(new AgentMetadata { AllowToolInvocation = true });

        // Only the agent is allow-listed. The tool below stands in for one the agent uses internally.
        var services = CreateServices(configureOptions: options => options.Agents = ["researcher"]);
        var orchestrator = new RecordingOrchestrator("The agent used its own tools.");

        AddLocalTool(services, "internal-key", new TestAIFunction("internal-tool"));
        AddAgents(services, agent);
        AddOrchestration(services, orchestrator);

        using var serviceProvider = services.BuildServiceProvider();

        // The agent is listed; the tool it uses internally is not.
        var listed = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["researcher"], listed.Tools.Select(tool => tool.Name));

        var result = await InvokeCallToolHandlerAsync(
            serviceProvider,
            "researcher",
            AgentPrompt("Find the latest figures."),
            cancellationToken: TestContext.Current.CancellationToken);

        // Reaching the orchestrator at all is the proof: the tool-less path never builds an orchestration
        // context, so the agent ran with its profile's tools enabled.
        Assert.True(orchestrator.WasExecuted);
        Assert.Contains("The agent used its own tools.", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);

        // The internal tool remains unreachable by name, which is the whole point of exposing the agent
        // instead of the tools it happens to use.
        await Assert.ThrowsAsync<McpException>(async () =>
            await InvokeCallToolHandlerAsync(
                serviceProvider,
                "internal-tool",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies that an agent which has not opted into tool invocation still runs, taking the tool-less path
    /// rather than the orchestrator. Exposure over MCP does not silently grant an agent more than its own
    /// profile allows.
    /// </summary>
    [Fact]
    public async Task CallToolHandler_AgentWithoutToolInvocation_DoesNotRunTheOrchestrator()
    {
        var services = CreateServices(configureOptions: options => options.Agents = ["researcher"]);
        var orchestrator = new RecordingOrchestrator("Should not run.");

        // No AgentMetadata at all, so AllowToolInvocation is false.
        AddAgents(services, CreateAgent("researcher"));
        AddOrchestration(services, orchestrator);
        var completionService = AddToollessCompletion(services, "The agent answered without tools.");

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeCallToolHandlerAsync(
            serviceProvider,
            "researcher",
            AgentPrompt("Find the latest figures."),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(orchestrator.WasExecuted);
        Assert.Contains("The agent answered without tools.", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);

        // The tool-less path disables tools on the agent's own context, which is what "did not opt in" means.
        Assert.True(completionService.LastContext?.DisableTools);
    }

    /// <summary>
    /// Registers the services the tool-less agent path needs, and returns the recording completion service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="response">The assistant text the completion returns.</param>
    /// <returns>The recording completion service.</returns>
    private static RecordingCompletionService AddToollessCompletion(IServiceCollection services, string response)
    {
        var completionService = new RecordingCompletionService(response);

        var contextBuilder = new Mock<IAICompletionContextBuilder>();
        contextBuilder
            .Setup(value => value.BuildAsync(It.IsAny<object>(), It.IsAny<Action<AICompletionContext>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AICompletionContext());

        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager
            .Setup(value => value.ResolveSlotAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIDeployment { ItemId = "deployment", Name = "chat" });

        services.AddSingleton<IAICompletionService>(completionService);
        services.AddSingleton(contextBuilder.Object);
        services.AddSingleton(deploymentManager.Object);

        return completionService;
    }

    /// <summary>
    /// Registers the orchestration services an agent needs to run with its own tools.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="orchestrator">The orchestrator to resolve.</param>
    private static void AddOrchestration(IServiceCollection services, RecordingOrchestrator orchestrator)
    {
        var contextBuilder = new Mock<IOrchestrationContextBuilder>();
        contextBuilder
            .Setup(value => value.BuildAsync(It.IsAny<object>(), It.IsAny<Action<OrchestrationContext>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationContext());

        var resolver = new Mock<IOrchestratorResolver>();
        resolver
            .Setup(value => value.Resolve(It.IsAny<string>()))
            .Returns(orchestrator);

        services.AddSingleton(contextBuilder.Object);
        services.AddSingleton(resolver.Object);
    }

    /// <summary>
    /// Builds the argument dictionary an agent expects.
    /// </summary>
    /// <param name="prompt">The prompt to send to the agent.</param>
    /// <returns>The call arguments.</returns>
    private static Dictionary<string, JsonElement> AgentPrompt(string prompt)
    {
        return new Dictionary<string, JsonElement>
        {
            ["prompt"] = JsonSerializer.SerializeToElement(prompt),
        };
    }

    /// <summary>
    /// Creates an agent profile with a description, which exposure requires.
    /// </summary>
    /// <param name="name">The agent name.</param>
    /// <returns>The agent profile.</returns>
    private static AIProfile CreateAgent(string name)
    {
        return new AIProfile
        {
            ItemId = name,
            Name = name,
            Description = $"The {name} agent.",
            Type = AIProfileType.Agent,
        };
    }

    /// <summary>
    /// Registers a fake profile manager returning the supplied agents.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="agents">The agent profiles to return.</param>
    private static void AddAgents(IServiceCollection services, params AIProfile[] agents)
    {
        var manager = new Mock<IAIProfileManager>();
        manager
            .Setup(value => value.GetAsync(AIProfileType.Agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agents);

        services.AddSingleton(manager.Object);
    }

    /// <summary>
    /// Creates the MCP service collection and registers the CrestApps handlers.
    /// </summary>
    /// <param name="configureOptions">An optional delegate that configures the exposure settings.</param>
    /// <returns>The configured service collection.</returns>
    private static ServiceCollection CreateServices(
        Action<ServerToolOptions> configureOptions = null)
    {
        var services = new ServiceCollection();
        var builder = services.AddMcpServer();

        services.AddLogging();
        services.AddOptions<AIToolDefinitionOptions>();
        services.AddOptions<ServerToolOptions>();

        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        builder.WithCrestAppsHandlers();

        return services;
    }

    /// <summary>
    /// Registers a fake tool instance catalog returning the supplied instances.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="instances">The instances to return.</param>
    private static void AddToolInstances(IServiceCollection services, params AIToolInstance[] instances)
    {
        var catalog = new Mock<INamedCatalog<AIToolInstance>>();
        catalog
            .Setup(value => value.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(instances);

        services.AddSingleton(catalog.Object);
    }

    /// <summary>
    /// Registers a keyed tool instance source that produces a function named after the instance.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="sourceName">The registered source name.</param>
    private static void AddToolInstanceSource(IServiceCollection services, string sourceName)
    {
        services.AddKeyedSingleton<IAIToolInstanceSource>(sourceName, (_, _) => new TestToolInstanceSource());
    }

    /// <summary>
    /// Registers a local tool definition and keyed tool instance.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="registrationName">The keyed registration name.</param>
    /// <param name="tool">The local AI function.</param>
    /// <param name="hidden">Whether the tool is hidden.</param>
    /// <param name="isSystemTool">Whether the tool is a system tool.</param>
    private static void AddLocalTool(
        IServiceCollection services,
        string registrationName,
        AIFunction tool,
        bool hidden = false,
        bool isSystemTool = false)
    {
        AddLocalToolDefinition(services, registrationName, hidden, isSystemTool);
        services.AddKeyedSingleton<AITool>(registrationName, tool);
    }

    /// <summary>
    /// Registers a local tool definition without registering its keyed implementation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="registrationName">The keyed registration name.</param>
    /// <param name="hidden">Whether the tool is hidden.</param>
    /// <param name="isSystemTool">Whether the tool is a system tool.</param>
    private static void AddLocalToolDefinition(
        IServiceCollection services,
        string registrationName,
        bool hidden = false,
        bool isSystemTool = false)
    {
        services.Configure<AIToolDefinitionOptions>(options =>
        {
            options.SetTool(
                registrationName,
                new AIToolDefinitionEntry(typeof(TestAIFunction))
                {
                    Hidden = hidden,
                    IsSystemTool = isSystemTool,
                });
        });
    }

    /// <summary>
    /// Invokes the registered CrestApps list-tools handler.
    /// </summary>
    /// <param name="serviceProvider">The provider containing the registered handler.</param>
    /// <param name="requestServices">Optional request-scoped services.</param>
    /// <param name="cancellationToken">The cancellation token passed to the handler.</param>
    /// <returns>The list-tools result.</returns>
    private static async ValueTask<ListToolsResult> InvokeListToolsHandlerAsync(
        IServiceProvider serviceProvider,
        IServiceProvider requestServices = null,
        CancellationToken cancellationToken = default)
    {
        requestServices ??= serviceProvider;

        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var handler = options.Handlers.ListToolsHandler;
        var server = new Mock<McpServer>();

        Assert.NotNull(handler);
        server.SetupGet(instance => instance.Services).Returns(requestServices);

        var request = new RequestContext<ListToolsRequestParams>(
            server.Object,
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsList,
                Id = new RequestId("1"),
            },
            new ListToolsRequestParams())
        {
            Services = requestServices,
        };

        return await handler(request, cancellationToken);
    }

    /// <summary>
    /// Invokes the registered CrestApps call-tool handler.
    /// </summary>
    /// <param name="serviceProvider">The provider containing the registered handler.</param>
    /// <param name="toolName">The name of the tool to invoke.</param>
    /// <param name="arguments">The arguments supplied with the call.</param>
    /// <param name="cancellationToken">The cancellation token passed to the handler.</param>
    /// <returns>The call-tool result.</returns>
    private static async ValueTask<CallToolResult> InvokeCallToolHandlerAsync(
        IServiceProvider serviceProvider,
        string toolName,
        IDictionary<string, JsonElement> arguments = null,
        CancellationToken cancellationToken = default)
    {
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var handler = options.Handlers.CallToolHandler;
        var server = new Mock<McpServer>();

        Assert.NotNull(handler);
        server.SetupGet(instance => instance.Services).Returns(serviceProvider);

        var request = new RequestContext<CallToolRequestParams>(
            server.Object,
            new JsonRpcRequest
            {
                Method = RequestMethods.ToolsCall,
                Id = new RequestId("1"),
            },
            new CallToolRequestParams
            {
                Name = toolName,
                Arguments = arguments,
            })
        {
            Services = serviceProvider,
        };

        return await handler(request, cancellationToken);
    }

    /// <summary>
    /// A completion service that records the context it was given and returns a fixed assistant reply.
    /// </summary>
    private sealed class RecordingCompletionService : IAICompletionService
    {
        private readonly string _response;

        public RecordingCompletionService(string response)
        {
            _response = response;
        }

        public AICompletionContext LastContext { get; private set; }

        public Task<ChatResponse> CompleteAsync(
            AIDeployment deployment,
            IEnumerable<ChatMessage> messages,
            AICompletionContext context,
            CancellationToken cancellationToken = default)
        {
            LastContext = context;

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingAsync(
            AIDeployment deployment,
            IEnumerable<ChatMessage> messages,
            AICompletionContext context,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// An orchestrator that records whether it ran and streams a fixed response.
    /// </summary>
    private sealed class RecordingOrchestrator : IOrchestrator
    {
        private readonly string _response;

        public RecordingOrchestrator(string response)
        {
            _response = response;
        }

        public bool WasExecuted { get; private set; }

        public string Name => "recording";

        public async IAsyncEnumerable<ChatResponseUpdate> ExecuteStreamingAsync(
            OrchestrationContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;

            WasExecuted = true;

            yield return new ChatResponseUpdate(ChatRole.Assistant, _response);
        }
    }

    /// <summary>
    /// A tool that records the ambient invocation scope it observed while running.
    /// </summary>
    private sealed class ScopeProbeFunction : AIFunction
    {
        private static readonly JsonElement _schema = JsonSerializer.Deserialize<JsonElement>(
        """
        {
          "type": "object"
        }
        """);

        private readonly string _name;

        public ScopeProbeFunction(string name)
        {
            _name = name;
        }

        public bool SawScope { get; private set; }

        public int? ObservedAgentDepth { get; private set; }

        public override string Name => _name;

        public override string Description => "Records the ambient invocation scope.";

        public override JsonElement JsonSchema => _schema;

        protected override ValueTask<object> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            var context = AIInvocationScope.Current;

            SawScope = context is not null;
            ObservedAgentDepth = context?.AgentInvocationDepth;

            return ValueTask.FromResult<object>(string.Empty);
        }
    }

    private sealed class TestToolInstanceSource : IAIToolInstanceSource
    {
        /// <summary>
        /// Creates a function whose name mirrors the instance name so tests can assert on it.
        /// </summary>
        /// <param name="instance">The configured instance.</param>
        /// <returns>The produced function.</returns>
        public AITool CreateTool(AIToolInstance instance)
        {
            return new TestAIFunction(instance.Name);
        }
    }

    private sealed class TestAIFunction : AIFunction
    {
        private static readonly JsonElement _defaultSchema = JsonSerializer.Deserialize<JsonElement>(
        """
        {
          "type": "object"
        }
        """);

        private readonly string _description;
        private readonly JsonElement _jsonSchema;
        private readonly string _name;

        /// <summary>
        /// Initializes a test AI function.
        /// </summary>
        /// <param name="name">The protocol tool name.</param>
        /// <param name="description">The protocol tool description.</param>
        /// <param name="jsonSchema">The protocol input schema.</param>
        public TestAIFunction(
            string name,
            string description = "Test description",
            JsonElement? jsonSchema = null)
        {
            _name = name;
            _description = description;
            _jsonSchema = jsonSchema ?? _defaultSchema;
        }

        /// <summary>
        /// Gets the tool name.
        /// </summary>
        public override string Name => _name;

        /// <summary>
        /// Gets the tool description.
        /// </summary>
        public override string Description => _description;

        /// <summary>
        /// Gets the tool schema.
        /// </summary>
        public override JsonElement JsonSchema => _jsonSchema;

        /// <summary>
        /// Invokes the test tool.
        /// </summary>
        /// <param name="arguments">The function arguments.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>An empty result.</returns>
        protected override ValueTask<object> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<object>(string.Empty);
        }
    }
}
