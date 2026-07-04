using CrestApps.Core.AI;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Orchestration;

public sealed class DefaultToolRegistryDependencyTests
{
    [Fact]
    public async Task GetAllAsync_ResolvesLocalToolDependenciesFromSystemProvider()
    {
        var options = new AIToolDefinitionOptions();
        options.SetTool("create_content_item", new AIToolDefinitionEntry(typeof(AIFunction))
        {
            Name = "create_content_item",
            Description = "Create a content item",
        });
        options.Tools["create_content_item"].AddDependency("get_content_item_schema");

        var registry = CreateRegistry(options,
        [
            new TestToolRegistryProvider(
            [
                new ToolRegistryEntry { Id = "create_content_item", Name = "create_content_item", Source = ToolRegistryEntrySource.Local },
            ]),
            new TestToolRegistryProvider(
            [
                new ToolRegistryEntry { Id = "get_content_item_schema", Name = "get_content_item_schema", Source = ToolRegistryEntrySource.System },
            ]),
        ]);
        var context = new AICompletionContext { ToolNames = ["create_content_item"] };

        var result = await registry.GetAllAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["create_content_item", "get_content_item_schema"], result.Select(entry => entry.Name));
        Assert.Equal(["get_content_item_schema"], Assert.IsType<string[]>(context.AdditionalProperties[AICompletionContextKeys.DependencyToolNames]));
    }

    [Fact]
    public async Task GetAllAsync_ResolvesSystemToolDependenciesFromOtherProviders()
    {
        var options = new AIToolDefinitionOptions();
        options.SetTool("search_documents", new AIToolDefinitionEntry(typeof(AIFunction))
        {
            Name = "search_documents",
            Description = "Search documents",
            IsSystemTool = true,
        });
        options.Tools["search_documents"].AddDependency("format_document_result");

        var registry = CreateRegistry(options,
        [
            new TestToolRegistryProvider(
            [
                new ToolRegistryEntry { Id = "search_documents", Name = "search_documents", Source = ToolRegistryEntrySource.System },
            ]),
            new TestToolRegistryProvider(
            [
                new ToolRegistryEntry { Id = "format_document_result", Name = "format_document_result", Source = ToolRegistryEntrySource.Local },
            ]),
        ]);

        var result = await registry.GetAllAsync(new AICompletionContext(), TestContext.Current.CancellationToken);

        Assert.Equal(["search_documents", "format_document_result"], result.Select(entry => entry.Name));
    }

    [Fact]
    public async Task GetAllAsync_MissingDependencies_AreIgnoredSafely()
    {
        var options = new AIToolDefinitionOptions();
        options.SetTool("create_content_item", new AIToolDefinitionEntry(typeof(AIFunction))
        {
            Name = "create_content_item",
            Description = "Create a content item",
        });
        options.Tools["create_content_item"].AddDependency("missing_tool");

        var registry = CreateRegistry(options,
        [
            new TestToolRegistryProvider(
            [
                new ToolRegistryEntry { Id = "create_content_item", Name = "create_content_item", Source = ToolRegistryEntrySource.Local },
            ]),
        ]);
        var context = new AICompletionContext();

        var result = await registry.GetAllAsync(context, TestContext.Current.CancellationToken);

        var entry = Assert.Single(result);
        Assert.Equal("create_content_item", entry.Name);
        Assert.False(context.AdditionalProperties.ContainsKey(AICompletionContextKeys.DependencyToolNames));
    }

    private static DefaultToolRegistry CreateRegistry(
        AIToolDefinitionOptions options,
        IToolRegistryProvider[] providers)
    {
        return new DefaultToolRegistry(
            providers,
            Options.Create(options),
            new LuceneTextTokenizer(),
            NullLogger<DefaultToolRegistry>.Instance);
    }

    private sealed class TestToolRegistryProvider : IToolRegistryProvider
    {
        private readonly IReadOnlyList<ToolRegistryEntry> _entries;

        public TestToolRegistryProvider(IReadOnlyList<ToolRegistryEntry> entries)
        {
            _entries = entries;
        }

        public Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(
            AICompletionContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_entries);
        }
    }
}
