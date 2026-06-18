using CrestApps.Core.AI;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Tooling;

public sealed class AIToolDefinitionOptionsTests
{
    [Fact]
    public void AddCoreAITool_WithDependencies_PersistsBuilderConfiguration()
    {
        var options = CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("create_content_item")
                .WithDependencies("get_content_item_schema", "get_sample_content_item")
                .WithoutDependency("get_sample_content_item");
        });

        var entry = options.Tools["create_content_item"];

        Assert.Equal(["get_content_item_schema"], entry.Dependencies);
    }

    [Fact]
    public void ExpandToolNames_IncludesAvailableDependenciesRecursively()
    {
        var options = CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("create_content_item")
                .WithDependencies("get_content_item_schema", "missing_tool");
            services.AddCoreAITool<TestTool>("get_content_item_schema")
                .WithDependency("get_common_schema_fragment");
            services.AddCoreAITool<TestTool>("get_common_schema_fragment");
        });

        var expandedToolNames = options.ExpandToolNames(["create_content_item"]);

        Assert.Equal(
        [
            "create_content_item",
            "get_content_item_schema",
            "get_common_schema_fragment",
        ],
        expandedToolNames);
    }

    [Fact]
    public void ExpandToolNames_DeduplicatesSharedAndCircularDependencies()
    {
        var options = CreateOptions(services =>
        {
            services.AddCoreAITool<TestTool>("tool_a")
                .WithDependency("tool_b");
            services.AddCoreAITool<TestTool>("tool_b")
                .WithDependency("tool_a");
            services.AddCoreAITool<TestTool>("tool_c")
                .WithDependency("tool_b");
        });

        var expandedToolNames = options.ExpandToolNames(["tool_a", "tool_c"]);

        Assert.Equal(["tool_a", "tool_b", "tool_c"], expandedToolNames);
    }

    private static AIToolDefinitionOptions CreateOptions(Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();

        services.AddOptions();

        configureServices(services);

        using var serviceProvider = services.BuildServiceProvider();

        return serviceProvider.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value;
    }

    private sealed class TestTool : AITool
    {
        public override string Name => "test_tool";

        public override string Description => "Test tool";

        public override IReadOnlyDictionary<string, object> AdditionalProperties => new Dictionary<string, object>();
    }
}
