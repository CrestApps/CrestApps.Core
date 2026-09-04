using System.Security.Claims;
using System.Text.Json;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class DefaultToolMaterializerTests
{
    [Fact]
    public async Task MaterializeAsync_StablyPrioritizesNonMcpEntriesAndDropsDuplicateNames()
    {
        var localFirst = new TestAIFunction("local-first-tool");
        var systemShared = new TestAIFunction("system-shared-tool");
        var localLast = new TestAIFunction("local-last-tool");
        var mcpFirst = new TestAIFunction("mcp-first-tool");
        var mcpDuplicate = new TestAIFunction("system-shared-tool"); // Same name -> dropped.

        IReadOnlyList<ToolRegistryEntry> entries =
        [
            CreateEntry("mcp-first", "mcp-first-tool", ToolRegistryEntrySource.McpServer, mcpFirst),
            CreateEntry("local-first", "local-first-tool", ToolRegistryEntrySource.Local, localFirst),
            CreateEntry("system-shared", "system-shared-tool", ToolRegistryEntrySource.System, systemShared),
            CreateEntry("mcp-dup", "system-shared-tool", ToolRegistryEntrySource.McpServer, mcpDuplicate),
            CreateEntry("local-last", "local-last-tool", ToolRegistryEntrySource.Local, localLast),
        ];

        var result = await CreateMaterializer().MaterializeAsync(entries, ToolMaterializationOptions.Default, TestContext.Current.CancellationToken);

        // Local/system first (registration order), then MCP; the duplicate MCP name is dropped.
        Assert.Equal([localFirst, systemShared, localLast, mcpFirst], result.Tools);
        Assert.Empty(result.DeniedToolNames);
    }

    [Fact]
    public async Task MaterializeAsync_WithoutEnforcement_DoesNotConsultEvaluator()
    {
        var evaluator = new RecordingToolAccessEvaluator { DenyAll = true };
        var listable = new TestAIFunction("listable-tool");
        IReadOnlyList<ToolRegistryEntry> entries =
        [
            CreateEntry("listable", "listable", ToolRegistryEntrySource.Local, listable),
        ];

        var result = await CreateMaterializer(
            evaluator,
            CreateToolDefinitions(services => services.AddCoreAITool<RegistrationTestTool>("listable").Selectable()))
            .MaterializeAsync(entries, ToolMaterializationOptions.Default, TestContext.Current.CancellationToken);

        Assert.Empty(evaluator.ToolNames);
        Assert.Equal([listable], result.Tools);
    }

    [Fact]
    public async Task MaterializeAsync_WithEnforcement_ExcludesDeniedListableToolsAndReportsThem()
    {
        var evaluator = new RecordingToolAccessEvaluator { DeniedToolNames = { "denied" } };
        var allowed = new TestAIFunction("allowed-tool");
        IReadOnlyList<ToolRegistryEntry> entries =
        [
            CreateEntry("denied", "denied", ToolRegistryEntrySource.Local, new TestAIFunction("denied-tool")),
            CreateEntry("allowed", "allowed", ToolRegistryEntrySource.Local, allowed),
        ];

        var result = await CreateMaterializer(
            evaluator,
            CreateToolDefinitions(services =>
            {
                services.AddCoreAITool<RegistrationTestTool>("denied").Selectable();
                services.AddCoreAITool<RegistrationTestTool>("allowed").Selectable();
            }))
            .MaterializeAsync(
                entries,
                new ToolMaterializationOptions { EnforceListableAccess = true, User = TestUser() },
                TestContext.Current.CancellationToken);

        Assert.Equal([allowed], result.Tools);
        Assert.Equal(["denied"], result.DeniedToolNames);
    }

    [Fact]
    public async Task MaterializeAsync_WithEnforcementButNullUser_KeepsEveryTool()
    {
        var evaluator = new RecordingToolAccessEvaluator { DenyAll = true };
        var listable = new TestAIFunction("listable-tool");
        IReadOnlyList<ToolRegistryEntry> entries =
        [
            CreateEntry("listable", "listable", ToolRegistryEntrySource.Local, listable),
        ];

        var result = await CreateMaterializer(
            evaluator,
            CreateToolDefinitions(services => services.AddCoreAITool<RegistrationTestTool>("listable").Selectable()))
            .MaterializeAsync(
                entries,
                new ToolMaterializationOptions { EnforceListableAccess = true, User = null },
                TestContext.Current.CancellationToken);

        Assert.Empty(evaluator.ToolNames);
        Assert.Equal([listable], result.Tools);
    }

    [Fact]
    public async Task MaterializeAsync_SkipsEntriesWithoutFactory()
    {
        var tool = new TestAIFunction("has-factory-tool");
        var missing = CreateEntry("missing", "missing", ToolRegistryEntrySource.Local, new TestAIFunction("missing-tool"));
        missing.CreateAsync = null;
        IReadOnlyList<ToolRegistryEntry> entries =
        [
            missing,
            CreateEntry("has-factory", "has-factory", ToolRegistryEntrySource.Local, tool),
        ];

        var result = await CreateMaterializer().MaterializeAsync(entries, ToolMaterializationOptions.Default, TestContext.Current.CancellationToken);

        Assert.Equal([tool], result.Tools);
    }

    private static DefaultToolMaterializer CreateMaterializer(
        IAIToolAccessEvaluator evaluator = null,
        IOptions<AIToolDefinitionOptions> toolDefinitions = null)
    {
        return new DefaultToolMaterializer(
            evaluator ?? new RecordingToolAccessEvaluator(),
            toolDefinitions ?? CreateToolDefinitions(_ => { }),
            new EmptyServiceProvider(),
            NullLogger<DefaultToolMaterializer>.Instance);
    }

    private static ClaimsPrincipal TestUser()
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], "Test"));

    private static IOptions<AIToolDefinitionOptions> CreateToolDefinitions(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        configure(services);

        using var serviceProvider = services.BuildServiceProvider();

        return Options.Create(serviceProvider.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value);
    }

    private static ToolRegistryEntry CreateEntry(string id, string name, ToolRegistryEntrySource source, AITool tool)
    {
        return new ToolRegistryEntry
        {
            Id = id,
            Name = name,
            Source = source,
            CreateAsync = _ => new ValueTask<AITool>(tool),
        };
    }

    private sealed class RecordingToolAccessEvaluator : IAIToolAccessEvaluator
    {
        public List<string> ToolNames { get; } = [];

        public HashSet<string> DeniedToolNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool DenyAll { get; set; }

        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, string toolName)
        {
            ToolNames.Add(toolName);

            return Task.FromResult(!DenyAll && !DeniedToolNames.Contains(toolName));
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object GetService(Type serviceType) => null;
    }

    private sealed class RegistrationTestTool : AIFunction
    {
        public override string Name => "registration-test-tool";

        public override string Description => Name;

        public override JsonElement JsonSchema => JsonSerializer.Deserialize<JsonElement>("{}");

        protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
            => new(Name);
    }

    private sealed class TestAIFunction : AIFunction
    {
        public TestAIFunction(string name)
        {
            Name = name;
        }

        public override string Name { get; }

        public override string Description => Name;

        public override JsonElement JsonSchema => JsonSerializer.Deserialize<JsonElement>("{}");

        protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
            => new(Name);
    }
}
