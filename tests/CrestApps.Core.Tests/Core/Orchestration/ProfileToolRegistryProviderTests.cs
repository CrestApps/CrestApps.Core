using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class ProfileToolRegistryProviderTests
{
    [Fact]
    public async Task GetToolsAsync_SelectedToolReturnsOnlyDirectProfileEntries()
    {
        var options = new AIToolDefinitionOptions();
        options.SetTool("create_content_item", new AIToolDefinitionEntry(typeof(AIFunction))
        {
            Name = "create_content_item",
            Description = "Create a content item",
        });
        options.SetTool("get_content_item_schema", new AIToolDefinitionEntry(typeof(AIFunction))
        {
            Name = "get_content_item_schema",
            Description = "Get the content type schema",
            IsSystemTool = true,
        });
        options.Tools["create_content_item"].AddDependency("get_content_item_schema");

        var provider = new ProfileToolRegistryProvider(Options.Create(options));

        var result = await provider.GetToolsAsync(
            new AICompletionContext { ToolNames = ["create_content_item"] },
            TestContext.Current.CancellationToken);

        var entry = Assert.Single(result);
        Assert.Equal("create_content_item", entry.Name);
        Assert.Equal(ToolRegistryEntrySource.Local, entry.Source);
    }

    [Fact]
    public async Task GetToolsAsync_SelectedSystemToolIsIgnored()
    {
        var options = new AIToolDefinitionOptions();
        options.SetTool("search_documents", new AIToolDefinitionEntry(typeof(AIFunction))
        {
            Name = "search_documents",
            Description = "Search documents",
            IsSystemTool = true,
        });

        var provider = new ProfileToolRegistryProvider(Options.Create(options));

        var result = await provider.GetToolsAsync(
            new AICompletionContext { ToolNames = ["search_documents"] },
            TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }
}
