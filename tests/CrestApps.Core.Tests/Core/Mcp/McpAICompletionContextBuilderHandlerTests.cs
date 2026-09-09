using CrestApps.Core.AI.Mcp.Handlers;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Tests.Core.Mcp;

/// <summary>
/// The profile's MCP selection has to reach the registry provider intact, and a profile saved before per-tool
/// selection existed has to keep taking every tool.
/// </summary>
public sealed class McpAICompletionContextBuilderHandlerTests
{
    [Fact]
    public async Task BuildingAsync_CopiesConnectionsAndPerConnectionToolSelection()
    {
        var profile = new AIProfile();
        profile.Put(new AIProfileMcpMetadata
        {
            ConnectionIds = ["conn-1", "conn-2"],
            ToolNames = new Dictionary<string, string[]> { ["conn-1"] = ["lookup_account", "update_address"] },
        });

        var context = new AICompletionContext();

        await new McpAICompletionContextBuilderHandler().BuildingAsync(new AICompletionContextBuildingContext(profile, context));

        Assert.Equal(["conn-1", "conn-2"], context.McpConnectionIds);
        Assert.Equal(["lookup_account", "update_address"], context.McpToolNames["conn-1"]);

        // conn-2 has no entry: it takes every tool it exposes.
        Assert.False(context.McpToolNames.ContainsKey("conn-2"));
    }

    [Fact]
    public async Task BuildingAsync_ProfileSavedBeforeToolSelectionExisted_LeavesSelectionUnset()
    {
        var profile = new AIProfile();
        profile.Put(new AIProfileMcpMetadata { ConnectionIds = ["conn-1"] });

        var context = new AICompletionContext();

        await new McpAICompletionContextBuilderHandler().BuildingAsync(new AICompletionContextBuildingContext(profile, context));

        Assert.Equal(["conn-1"], context.McpConnectionIds);
        Assert.Null(context.McpToolNames);
    }

    [Fact]
    public async Task BuildingAsync_ResourceWithoutMcpMetadata_TouchesNothing()
    {
        var context = new AICompletionContext();

        await new McpAICompletionContextBuilderHandler().BuildingAsync(new AICompletionContextBuildingContext(new AIProfile(), context));

        Assert.Null(context.McpConnectionIds);
        Assert.Null(context.McpToolNames);
    }
}
