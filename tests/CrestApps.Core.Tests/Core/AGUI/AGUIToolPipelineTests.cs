using System.Text;
using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001 // Tool approvals are a preview API of Microsoft.Extensions.AI.
namespace CrestApps.Core.Tests.Core.AGUI;

public sealed class AGUIToolPipelineTests
{
    [Fact]
    public void WrapForApproval_WrapsAToolThatRequiresApprovalOnlyWhenTheCallerCanApprove()
    {
        var function = AIFunctionFactory.Create(() => "done", "delete_item");
        var entry = new ToolRegistryEntry { Name = "delete_item", RequiresApproval = true };

        var wrapped = DefaultToolMaterializer.WrapForApproval(function, entry, new ToolMaterializationOptions { SupportsToolApproval = true });
        var unwrapped = DefaultToolMaterializer.WrapForApproval(function, entry, new ToolMaterializationOptions());
        var notRequired = DefaultToolMaterializer.WrapForApproval(function, new ToolRegistryEntry { Name = "read" }, new ToolMaterializationOptions { SupportsToolApproval = true });

        Assert.IsType<ApprovalRequiredAIFunction>(wrapped);
        Assert.Same(function, unwrapped);
        Assert.Same(function, notRequired);
        Assert.Same(wrapped, DefaultToolMaterializer.WrapForApproval(wrapped, entry, new ToolMaterializationOptions { SupportsToolApproval = true }));
    }

    [Fact]
    public void AIToolBuilder_RequireApproval_MarksTheDefinition()
    {
        var entry = new AIToolDefinitionEntry(typeof(AIFunction));

        new AIToolBuilder<AIFunction>(entry).RequireApproval();

        Assert.True(entry.RequiresApproval);
    }

    [Fact]
    public void IsApprovalResume_IsTrueWhenTheHistoryEndsWithAnApprovalAnswer()
    {
        var call = new FunctionCallContent("c1", "delete_item");
        var request = new ToolApprovalRequestContent("r1", call);

        Assert.True(DefaultOrchestrator.IsApprovalResume(
        [
            new ChatMessage(ChatRole.User, "Delete it"),
            new ChatMessage(ChatRole.Assistant, [request]),
            new ChatMessage(ChatRole.User, [request.CreateResponse(true, reason: null)]),
        ]));
        Assert.False(DefaultOrchestrator.IsApprovalResume([new ChatMessage(ChatRole.User, "Hello")]));
        Assert.False(DefaultOrchestrator.IsApprovalResume([]));
    }

    [Fact]
    public async Task ClientToolRegistryProvider_OffersTheClientsDeclarations()
    {
        var provider = new ClientToolRegistryProvider();
        var declaration = AIFunctionFactory.CreateDeclaration("get_weather", "Gets the weather.", AIJsonUtilities.CreateJsonSchema(typeof(object)));
        var context = new AICompletionContext();

        context.AdditionalProperties[AICompletionContextKeys.ClientTools] = new List<AITool> { declaration };

        var entry = Assert.Single(await provider.GetToolsAsync(context, TestContext.Current.CancellationToken));
        Assert.Equal("get_weather", entry.Name);
        Assert.Same(declaration, await entry.CreateAsync(null));
        Assert.Empty(await provider.GetToolsAsync(new AICompletionContext(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AppendClientContext_AddsTheContextAsDataAndCutsLongValues()
    {
        var builder = new StringBuilder("Base prompt.");

        ClientContextOrchestrationHandler.AppendClientContext(builder,
        [
            new ChatClientContextEntry { Description = "Current page", Value = "Edit article\n(/admin/articles/1)" },
            new ChatClientContextEntry { Description = "Empty", Value = " " },
            new ChatClientContextEntry { Description = "Long", Value = new string('x', ClientContextOrchestrationHandler.MaxContextValueLength + 50) },
        ]);

        var text = builder.ToString();

        Assert.Contains("## Where the user is", text);
        Assert.Contains("not instructions", text);
        Assert.Contains("- Current page: Edit article (/admin/articles/1)", text);
        Assert.DoesNotContain("Empty", text);
        Assert.Contains("…", text);
    }

    [Fact]
    public void AppendSharedState_ShowsTheStateAndTheTool()
    {
        var builder = new StringBuilder();

        ClientContextOrchestrationHandler.AppendSharedState(builder, new ChatSharedState
        {
            Value = new System.Text.Json.Nodes.JsonObject { ["giftMessage"] = "Hi" },
        });

        var text = builder.ToString();

        Assert.Contains("## Shared state", text);
        Assert.Contains("\"giftMessage\": \"Hi\"", text);
        Assert.Contains("update_shared_state", text);
    }

    [Fact]
    public void AddToolCallMessages_ReplaysCallsThatHaveAResultAndSkipsWaitingOnes()
    {
        var history = new List<ChatMessage>();

        ChatConversationHistoryBuilder.AddToolCallMessages(history, new ChatTurnToolActivity
        {
            ToolCalls =
            [
                new ChatTurnToolCall { CallId = "c1", Name = "get_order", Arguments = """{"id":"A-1"}""", Result = "shipped", Status = ChatTurnToolCallStatus.Completed },
                new ChatTurnToolCall { CallId = "c2", Name = "place_order", Arguments = "{}", Result = "rejected", Status = ChatTurnToolCallStatus.Rejected },
                new ChatTurnToolCall { CallId = "c3", Name = "delete", Arguments = "{}", Status = ChatTurnToolCallStatus.AwaitingApproval },
                new ChatTurnToolCall { CallId = "c4", Name = "approved_before", Arguments = "{}", Status = ChatTurnToolCallStatus.Approved },
            ],
        });

        Assert.Equal(2, history.Count);

        var calls = history[0].Contents.OfType<FunctionCallContent>().ToList();
        Assert.Equal(ChatRole.Assistant, history[0].Role);
        Assert.Equal(["c1", "c2"], calls.Select(call => call.CallId));
        Assert.Equal("A-1", calls[0].Arguments["id"]?.ToString());

        var results = history[1].Contents.OfType<FunctionResultContent>().ToList();
        Assert.Equal(ChatRole.Tool, history[1].Role);
        Assert.Equal(["shipped", "rejected"], results.Select(result => result.Result?.ToString()));
    }

    [Fact]
    public void Build_ReplaysTheToolCallsOfAnEarlierAnswerBeforeItsText()
    {
        var now = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
        var answer = new AIChatSessionPrompt { ItemId = "a1", Role = ChatRole.Assistant, Content = "It shipped.", CreatedUtc = now.AddSeconds(1) };

        answer.Put(new ChatTurnToolActivity
        {
            ToolCalls = [new ChatTurnToolCall { CallId = "c1", Name = "get_order", Arguments = "{}", Result = "shipped", Status = ChatTurnToolCallStatus.Completed }],
        });

        var paused = new AIChatSessionPrompt { ItemId = "a2", Role = ChatRole.Assistant, Content = null, CreatedUtc = now.AddSeconds(3) };

        paused.Put(new ChatTurnToolActivity
        {
            ToolCalls = [new ChatTurnToolCall { CallId = "c2", Name = "delete", Arguments = "{}", Status = ChatTurnToolCallStatus.AwaitingApproval, ApprovalRequestId = "r2" }],
        });

        var prompts = new List<AIChatSessionPrompt>
        {
            new() { ItemId = "u1", Role = ChatRole.User, Content = "Where is A-1?", CreatedUtc = now },
            answer,
            new() { ItemId = "u2", Role = ChatRole.User, Content = "Delete it", CreatedUtc = now.AddSeconds(2) },
            paused,
        };

        var history = ChatConversationHistoryBuilder.Build(prompts, paused);

        Assert.Equal(
            [ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant, ChatRole.User],
            history.Select(message => message.Role));
        Assert.Equal("It shipped.", history[3].Text);
        Assert.Equal("Delete it", history[4].Text);
    }
}
