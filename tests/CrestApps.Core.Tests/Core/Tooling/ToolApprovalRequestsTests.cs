using System.Text.Json;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001 // Tool approvals are a preview API of Microsoft.Extensions.AI.
namespace CrestApps.Core.Tests.Core.Tooling;

/// <summary>
/// Covers which tool approval requests are marked as needing no approval of their own. Once one call of a round
/// requires an approval, every call of the round comes as an approval request; only a call to a function the request
/// offers, that the server runs and that does not require an approval, is marked.
/// </summary>
public sealed class ToolApprovalRequestsTests
{
    /// <summary>
    /// Verifies that only the call to a function that needs no approval is marked, and that a call to a tool that
    /// requires one, to a tool the client runs, or to a tool the request does not offer keeps asking.
    /// </summary>
    [Fact]
    public void MarkCallsThatNeedNoApproval_MarksOnlyACallToAFunctionThatNeedsNoApproval()
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(() => "Items: 7, 8", "list_items"),
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create((int id) => $"Deleted item {id}.", "delete_item")),
            AIFunctionFactory.CreateDeclaration("open_page", "Opens a page.", JsonElement.Parse("""{"type":"object"}""")),
        };
        var list = CreateRequest("list_items");
        var delete = CreateRequest("delete_item");
        var clientTool = CreateRequest("open_page");
        var unknown = CreateRequest("send_email");

        ToolApprovalRequests.MarkCallsThatNeedNoApproval([list, delete, clientTool, unknown, new TextContent("text")], tools);

        Assert.True(ToolApprovalRequests.NeedsNoApproval(list));
        Assert.False(ToolApprovalRequests.NeedsNoApproval(delete));
        Assert.False(ToolApprovalRequests.NeedsNoApproval(clientTool));
        Assert.False(ToolApprovalRequests.NeedsNoApproval(unknown));
    }

    /// <summary>
    /// Verifies that nothing is marked when the request offered no tools, so every call keeps asking.
    /// </summary>
    [Fact]
    public void MarkCallsThatNeedNoApproval_WithoutTools_MarksNothing()
    {
        var list = CreateRequest("list_items");

        ToolApprovalRequests.MarkCallsThatNeedNoApproval([list], tools: null);

        Assert.False(ToolApprovalRequests.NeedsNoApproval(list));
        Assert.Null(list.AdditionalProperties);
    }

    private static ToolApprovalRequestContent CreateRequest(string toolName)
    {
        return new ToolApprovalRequestContent($"ficc_{toolName}", new FunctionCallContent($"call-{toolName}", toolName));
    }
}
