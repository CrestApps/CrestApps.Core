using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class ProfileToolRegistryProviderTests
{
    [Fact]
    public async Task GetToolsAsync_SelectedToolIncludesRegisteredDependencies()
    {
        var options = new AIToolDefinitionOptions();
        options.SetTool("create_content_item", new AIToolDefinitionEntry(typeof(AIFunction))
        {
            Name = "create_content_item",
            Description = "Create a content item",
            IsSystemTool = false,
        });
        options.SetTool("get_content_item_schema", new AIToolDefinitionEntry(typeof(AIFunction))
        {
            Name = "get_content_item_schema",
            Description = "Get the content type schema",
            IsSystemTool = true,
            Purpose = AIToolPurposes.DataSourceSearch,
        });
        options.SetTool("get_common_schema_fragment", new AIToolDefinitionEntry(typeof(AIFunction))
        {
            Name = "get_common_schema_fragment",
            Description = "Get shared schema fragments",
            IsSystemTool = false,
        });

        options.Tools["create_content_item"].AddDependency("get_content_item_schema");
        options.Tools["get_content_item_schema"].AddDependency("get_common_schema_fragment");

        var provider = new ProfileToolRegistryProvider(Options.Create(options));

        var result = await provider.GetToolsAsync(
            new AICompletionContext { ToolNames = ["create_content_item"] },
            TestContext.Current.CancellationToken);

        Assert.Equal(
        [
            "create_content_item",
            "get_content_item_schema",
            "get_common_schema_fragment",
        ],
        result.Select(entry => entry.Name));
        Assert.Collection(
            result,
            entry => Assert.Equal(ToolRegistryEntrySource.Local, entry.Source),
            entry => Assert.Equal(ToolRegistryEntrySource.System, entry.Source),
            entry => Assert.Equal(ToolRegistryEntrySource.Local, entry.Source));

    }

    [Fact]
    public async Task GetToolsAsync_MissingDependencies_AreIgnored()
    {
        var options = new AIToolDefinitionOptions();
        options.SetTool("create_content_item", new AIToolDefinitionEntry(typeof(AIFunction))
        {
            Name = "create_content_item",
            Description = "Create a content item",
            IsSystemTool = false,
        });
        options.Tools["create_content_item"].AddDependency("missing_tool");

        var provider = new ProfileToolRegistryProvider(Options.Create(options));

        var result = await provider.GetToolsAsync(
            new AICompletionContext { ToolNames = ["create_content_item"] },
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(result);
        Assert.Equal("create_content_item", entry.Name);

    }
}
