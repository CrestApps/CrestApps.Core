using System.Text.Json;
using CrestApps.Core.AI.Mcp;
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
                TestContext.Current.CancellationToken));
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
            TestContext.Current.CancellationToken);

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
            TestContext.Current.CancellationToken);

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
            TestContext.Current.CancellationToken);

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
            TestContext.Current.CancellationToken);

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
                TestContext.Current.CancellationToken));
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
                TestContext.Current.CancellationToken));
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
    /// <param name="cancellationToken">The cancellation token passed to the handler.</param>
    /// <returns>The call-tool result.</returns>
    private static async ValueTask<CallToolResult> InvokeCallToolHandlerAsync(
        IServiceProvider serviceProvider,
        string toolName,
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
            })
        {
            Services = serviceProvider,
        };

        return await handler(request, cancellationToken);
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
