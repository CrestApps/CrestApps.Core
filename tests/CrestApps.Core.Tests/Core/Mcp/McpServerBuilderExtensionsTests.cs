using System.Text.Json;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;

namespace CrestApps.Core.Tests.Core.Mcp;

public sealed class McpServerBuilderExtensionsTests
{
    /// <summary>
    /// Verifies that visible local tools retain registration order, precede SDK tools, and hidden tools are omitted.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_ReturnsVisibleLocalToolsFirstAndOmitsHiddenTools()
    {
        var services = CreateServices(
            CreateSdkTool("sdk-first"),
            CreateSdkTool("sdk-second"));

        AddLocalTool(services, "local-first-key", new TestAIFunction("local-first"));
        AddLocalTool(services, "hidden-key", new TestAIFunction("hidden"), hidden: true);
        AddLocalTool(services, "local-second-key", new TestAIFunction("local-second"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["local-first", "local-second", "sdk-first", "sdk-second"],
            result.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// Verifies that keyed local tool creation failures are logged and skipped.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_LogsAndSkipsKeyedServiceCreationFailures()
    {
        var services = CreateServices();
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
    /// Verifies that duplicate protocol names produced by distinct local registrations remain in the result.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_PreservesDuplicateNamesProducedByLocalTools()
    {
        var services = CreateServices(CreateSdkTool("sdk"));

        AddLocalTool(services, "first-key", new TestAIFunction("duplicate"));
        AddLocalTool(services, "second-key", new TestAIFunction("duplicate"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["duplicate", "duplicate", "sdk"], result.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// Verifies that SDK tools are appended in service enumeration order.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_AppendsSdkToolsInEnumerationOrder()
    {
        var services = CreateServices(
            CreateSdkTool("sdk-third"),
            CreateSdkTool("sdk-first"),
            CreateSdkTool("sdk-second"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["sdk-third", "sdk-first", "sdk-second"],
            result.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// Verifies that a local tool takes precedence over an SDK tool with the same exact name.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_SkipsSdkToolsThatDuplicateLocalNames()
    {
        var duplicateSdkTool = CreateSdkTool("duplicate", "SDK duplicate");
        var services = CreateServices(duplicateSdkTool, CreateSdkTool("sdk"));
        var localDescription = "Local duplicate";

        AddLocalTool(
            services,
            "local-key",
            new TestAIFunction("duplicate", localDescription));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["duplicate", "sdk"], result.Tools.Select(tool => tool.Name));
        Assert.Equal(localDescription, result.Tools[0].Description);
        Assert.DoesNotContain(result.Tools, tool => ReferenceEquals(tool, duplicateSdkTool.ProtocolTool));
    }

    /// <summary>
    /// Verifies that later SDK tools with duplicate names are skipped while the first instance is retained.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_SkipsDuplicateNamesWithinSdkTools()
    {
        var first = CreateSdkTool("duplicate", "first");
        var second = CreateSdkTool("duplicate", "second");
        var unique = CreateSdkTool("unique");
        var services = CreateServices(first, second, unique);

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["duplicate", "unique"], result.Tools.Select(tool => tool.Name));
        Assert.Same(first.ProtocolTool, result.Tools[0]);
        Assert.DoesNotContain(result.Tools, tool => ReferenceEquals(tool, second.ProtocolTool));
    }

    /// <summary>
    /// Verifies that duplicate matching uses ordinal case-sensitive equality.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_TreatsToolNamesAsOrdinalCaseSensitive()
    {
        var services = CreateServices(
            CreateSdkTool("casetool"),
            CreateSdkTool("CaseTool"));

        AddLocalTool(services, "local-key", new TestAIFunction("CaseTool"));

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["CaseTool", "casetool"], result.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// Verifies that a service provider returning a null SDK tool enumerable produces an empty result.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_AllowsNullSdkToolEnumerable()
    {
        var services = CreateServices();

        using var serviceProvider = services.BuildServiceProvider();
        var requestServices = new NullSdkEnumerableServiceProvider(serviceProvider);

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            requestServices,
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Tools);
    }

    /// <summary>
    /// Verifies that the default DI empty SDK tool enumerable leaves local tools unchanged.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_AllowsDefaultEmptySdkToolEnumerable()
    {
        var services = CreateServices();

        AddLocalTool(services, "local-key", new TestAIFunction("local"));

        using var serviceProvider = services.BuildServiceProvider();
        var sdkTools = serviceProvider.GetRequiredService<IEnumerable<McpServerTool>>();

        Assert.Empty(sdkTools);

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["local"], result.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// Verifies that local metadata values and SDK protocol tool instances retain their identity.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_PreservesToolSchemaDescriptionAndSdkProtocolIdentity()
    {
        var schema = JsonSerializer.Deserialize<JsonElement>(
        """
        {
          "type": "object",
          "properties": {
            "value": {
              "type": "integer"
            }
          }
        }
        """);
        var description = new string("Local description".ToCharArray());
        var localTool = new TestAIFunction("local", description, schema);
        var sdkTool = CreateSdkTool("sdk");
        var services = CreateServices(sdkTool);

        AddLocalTool(services, "local-key", localTool);

        using var serviceProvider = services.BuildServiceProvider();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(description, result.Tools[0].Description);
        Assert.Equal(localTool.JsonSchema, result.Tools[0].InputSchema);
        Assert.Same(sdkTool.ProtocolTool, result.Tools[1]);
    }

    /// <summary>
    /// Verifies the current synchronous list handler behavior when passed an already-canceled token.
    /// </summary>
    [Fact]
    public async Task ListToolsHandler_DoesNotObserveCancellation()
    {
        var services = CreateServices(CreateSdkTool("sdk"));

        AddLocalTool(services, "local-key", new TestAIFunction("local"));

        using var serviceProvider = services.BuildServiceProvider();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        var result = await InvokeListToolsHandlerAsync(
            serviceProvider,
            cancellationToken: cancellationTokenSource.Token);

        Assert.Equal(["local", "sdk"], result.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    /// Creates the MCP service collection and registers the CrestApps handlers.
    /// </summary>
    /// <param name="sdkTools">The SDK tools to register in enumeration order.</param>
    /// <returns>The configured service collection.</returns>
    private static ServiceCollection CreateServices(params McpServerTool[] sdkTools)
    {
        var services = new ServiceCollection();
        var builder = services.AddMcpServer();

        services.AddOptions<AIToolDefinitionOptions>();
        builder.WithTools(sdkTools);
        builder.WithCrestAppsHandlers();

        return services;
    }

    /// <summary>
    /// Registers a local tool definition and keyed tool instance.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="registrationName">The keyed registration name.</param>
    /// <param name="tool">The local AI function.</param>
    /// <param name="hidden">Whether the tool is hidden.</param>
    private static void AddLocalTool(
        IServiceCollection services,
        string registrationName,
        AIFunction tool,
        bool hidden = false)
    {
        AddLocalToolDefinition(services, registrationName, hidden);
        services.AddKeyedSingleton<AITool>(registrationName, tool);
    }

    /// <summary>
    /// Registers a local tool definition without registering its keyed implementation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="registrationName">The keyed registration name.</param>
    /// <param name="hidden">Whether the tool is hidden.</param>
    private static void AddLocalToolDefinition(
        IServiceCollection services,
        string registrationName,
        bool hidden = false)
    {
        services.Configure<AIToolDefinitionOptions>(options =>
        {
            options.SetTool(
                registrationName,
                new AIToolDefinitionEntry(typeof(TestAIFunction))
                {
                    Hidden = hidden,
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
    /// Creates an SDK MCP tool with the supplied protocol metadata.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <param name="description">The optional description.</param>
    /// <returns>The SDK MCP tool.</returns>
    private static McpServerTool CreateSdkTool(string name, string description = null)
    {
        return McpServerTool.Create(
            (Func<string>)(static () => string.Empty),
            new McpServerToolCreateOptions
            {
                Name = name,
                Description = description,
            });
    }

    private sealed class NullSdkEnumerableServiceProvider : IServiceProvider
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// Initializes a provider that masks the SDK tool enumerable.
        /// </summary>
        /// <param name="serviceProvider">The underlying service provider.</param>
        public NullSdkEnumerableServiceProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Resolves a service, returning null for the SDK tool enumerable.
        /// </summary>
        /// <param name="serviceType">The service type.</param>
        /// <returns>The resolved service.</returns>
        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IEnumerable<McpServerTool>))
            {
                return null;
            }

            return _serviceProvider.GetService(serviceType);
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
