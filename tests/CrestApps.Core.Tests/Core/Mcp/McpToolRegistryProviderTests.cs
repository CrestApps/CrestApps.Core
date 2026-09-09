using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Mcp;

/// <summary>
/// A profile may take only some of a connection's tools. These pin the three cases the selection map can
/// express, and that a profile saved before the map existed behaves exactly as it did.
/// </summary>
public sealed class McpToolRegistryProviderTests
{
    private const string ConnectionId = "conn-1";

    [Fact]
    public async Task GetToolsAsync_WithNoSelectionForTheConnection_OffersEveryTool()
    {
        var provider = CreateProvider("lookup_account", "update_address", "issue_refund");

        var entries = await provider.GetToolsAsync(new AICompletionContext { McpConnectionIds = [ConnectionId] }, TestContext.Current.CancellationToken);

        Assert.Equal(["lookup_account", "update_address", "issue_refund"], entries.Select(e => e.Name));
    }

    [Fact]
    public async Task GetToolsAsync_WithASubsetSelected_OffersOnlyThoseTools()
    {
        var provider = CreateProvider("lookup_account", "update_address", "issue_refund");
        var context = new AICompletionContext
        {
            McpConnectionIds = [ConnectionId],
            McpToolNames = new Dictionary<string, string[]> { [ConnectionId] = ["lookup_account", "update_address"] },
        };

        var entries = await provider.GetToolsAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["lookup_account", "update_address"], entries.Select(e => e.Name));
        Assert.All(entries, e => Assert.Equal(ConnectionId, e.SourceId));
    }

    [Fact]
    public async Task GetToolsAsync_WithAnEmptySelection_OffersNoToolsFromThatConnection()
    {
        // The connection is kept for its prompts and resources; it just contributes nothing the model can call.
        var provider = CreateProvider("lookup_account", "update_address");
        var context = new AICompletionContext
        {
            McpConnectionIds = [ConnectionId],
            McpToolNames = new Dictionary<string, string[]> { [ConnectionId] = [] },
        };

        var entries = await provider.GetToolsAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetToolsAsync_SelectionForAnotherConnection_DoesNotAffectThisOne()
    {
        var provider = CreateProvider("lookup_account", "update_address");
        var context = new AICompletionContext
        {
            McpConnectionIds = [ConnectionId],
            McpToolNames = new Dictionary<string, string[]> { ["some-other-connection"] = ["nothing"] },
        };

        var entries = await provider.GetToolsAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task GetToolsAsync_MatchesToolNamesExactly()
    {
        // MCP tool names are identifiers, so "Lookup_Account" is not "lookup_account".
        var provider = CreateProvider("lookup_account");
        var context = new AICompletionContext
        {
            McpConnectionIds = [ConnectionId],
            McpToolNames = new Dictionary<string, string[]> { [ConnectionId] = ["Lookup_Account"] },
        };

        var entries = await provider.GetToolsAsync(context, TestContext.Current.CancellationToken);

        Assert.Empty(entries);
    }

    private static McpToolRegistryProvider CreateProvider(params string[] toolNames)
    {
        var connection = new McpConnection { ItemId = ConnectionId };

        var metadata = new Mock<IMcpServerMetadataCacheProvider>();
        metadata
            .Setup(m => m.GetCapabilitiesAsync(It.IsAny<McpConnection>()))
            .ReturnsAsync(new McpServerCapabilities
            {
                Tools = toolNames.Select(name => new McpServerCapability { Name = name, Description = $"{name} description" }).ToList(),
            });

        var store = new Mock<ISourceCatalog<McpConnection>>();
        store
            .Setup(s => s.GetAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<IReadOnlyCollection<McpConnection>>([connection]));

        return new McpToolRegistryProvider(metadata.Object, store.Object, NullLogger<McpToolRegistryProvider>.Instance);
    }
}
