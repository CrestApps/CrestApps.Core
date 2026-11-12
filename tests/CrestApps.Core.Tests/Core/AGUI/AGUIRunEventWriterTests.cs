using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;
using CrestApps.Core.AI.AGUI;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001 // Tool approvals are a preview API of Microsoft.Extensions.AI.
namespace CrestApps.Core.Tests.Core.AGUI;

public sealed class AGUIRunEventWriterTests
{
    [Fact]
    public async Task WriteUpdateAsync_StreamsTextToolCallsAndResultsInOrder()
    {
        var events = new List<BaseEvent>();
        var writer = CreateWriter(events);

        await writer.WriteUpdateAsync(new ChatResponseUpdate(ChatRole.Assistant, "Let me check. "), "m1", TestContext.Current.CancellationToken);
        await writer.WriteUpdateAsync(new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("c1", "get_order", new Dictionary<string, object> { ["id"] = "A-1" })]), "m1", TestContext.Current.CancellationToken);
        await writer.WriteUpdateAsync(new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("c1", "shipped")]), "m1", TestContext.Current.CancellationToken);
        await writer.WriteUpdateAsync(new ChatResponseUpdate(ChatRole.Assistant, "It shipped."), "m1", TestContext.Current.CancellationToken);
        await writer.FinishAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                AGUIEventTypes.RunStarted,
                AGUIEventTypes.TextMessageStart,
                AGUIEventTypes.TextMessageContent,
                AGUIEventTypes.TextMessageEnd,
                AGUIEventTypes.ToolCallStart,
                AGUIEventTypes.ToolCallArgs,
                AGUIEventTypes.ToolCallEnd,
                AGUIEventTypes.ToolCallResult,
                AGUIEventTypes.TextMessageStart,
                AGUIEventTypes.TextMessageContent,
                AGUIEventTypes.TextMessageEnd,
                AGUIEventTypes.RunFinished,
            ],
            events.Select(evt => evt.Type));

        var textStarts = events.OfType<TextMessageStartEvent>().ToList();
        Assert.Equal("m1", textStarts[0].MessageId);
        Assert.Equal("m1-1", textStarts[1].MessageId);

        var start = Assert.Single(events.OfType<ToolCallStartEvent>());
        Assert.Equal("c1", start.ToolCallId);
        Assert.Equal("get_order", start.ToolCallName);
        Assert.Equal("m1", start.ParentMessageId);
        Assert.Equal("""{"id":"A-1"}""", Assert.Single(events.OfType<ToolCallArgsEvent>()).Delta);

        var call = Assert.Single(writer.ToolCalls);
        Assert.Equal(ChatTurnToolCallStatus.Completed, call.Status);
        Assert.Equal("shipped", call.Result);
        Assert.True(writer.HasText("m1"));

        var finished = Assert.IsType<RunFinishedEvent>(events[^1]);
        Assert.IsType<RunFinishedSuccessOutcome>(finished.Outcome);
        Assert.Equal("thread-1", finished.ThreadId);
        Assert.Equal("run-1", finished.RunId);
    }

    [Fact]
    public async Task WriteUpdateAsync_EndsWithAnInterruptForAToolApprovalRequest()
    {
        var events = new List<BaseEvent>();
        var writer = CreateWriter(events);
        var call = new FunctionCallContent("c2", "delete_item", new Dictionary<string, object> { ["id"] = "42" });

        await writer.WriteUpdateAsync(new ChatResponseUpdate(ChatRole.Assistant, [new ToolApprovalRequestContent("approval-1", call)]), "m1", TestContext.Current.CancellationToken);
        await writer.FinishAsync(TestContext.Current.CancellationToken);

        Assert.Contains(events, evt => evt is ToolCallStartEvent start && start.ToolCallId == "c2");

        var record = Assert.Single(writer.ToolCalls);
        Assert.Equal(ChatTurnToolCallStatus.AwaitingApproval, record.Status);
        Assert.Equal("approval-1", record.ApprovalRequestId);

        var outcome = Assert.IsType<RunFinishedInterruptOutcome>(Assert.IsType<RunFinishedEvent>(events[^1]).Outcome);
        var interrupt = Assert.Single(outcome.Interrupts);
        Assert.Equal("approval-1", interrupt.Id);
        Assert.Equal("c2", interrupt.ToolCallId);
        Assert.Equal(InterruptReasons.ToolCall, interrupt.Reason);
        Assert.Equal("delete_item", interrupt.Metadata.Value.GetProperty("toolName").GetString());
    }

    [Fact]
    public async Task WriteUpdateAsync_LeavesACallToAClientToolWaitingForTheClient()
    {
        var events = new List<BaseEvent>();
        var writer = new AGUIRunEventWriter("thread-1", "run-1", Collect(events), new HashSet<string>(["get_weather"]));

        await writer.WriteUpdateAsync(new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("c3", "get_weather")]), "m1", TestContext.Current.CancellationToken);
        await writer.FinishAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ChatTurnToolCallStatus.AwaitingClient, Assert.Single(writer.ToolCalls).Status);

        var outcome = Assert.IsType<RunFinishedSuccessOutcome>(Assert.IsType<RunFinishedEvent>(events[^1]).Outcome);
        Assert.Equal(["c3"], outcome.PendingToolCallIds);
    }

    [Fact]
    public async Task WriteUpdateAsync_ReportsTheResultOfARejectedCallAsRejected()
    {
        var writer = CreateWriter([]);
        writer.RejectedCallIds.Add("c4");

        await writer.WriteUpdateAsync(new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("c4", "place_order")]), "m1", TestContext.Current.CancellationToken);
        await writer.WriteUpdateAsync(new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("c4", "rejected")]), "m1", TestContext.Current.CancellationToken);

        Assert.Equal(ChatTurnToolCallStatus.Rejected, Assert.Single(writer.ToolCalls).Status);
    }

    [Fact]
    public async Task WriteUpdateAsync_StreamsReasoningAsReasoningMessages()
    {
        var events = new List<BaseEvent>();
        var writer = CreateWriter(events);

        await writer.WriteUpdateAsync(new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("Thinking it over.")]), "m1", TestContext.Current.CancellationToken);
        await writer.WriteUpdateAsync(new ChatResponseUpdate(ChatRole.Assistant, "Answer."), "m1", TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                AGUIEventTypes.RunStarted,
                AGUIEventTypes.ReasoningStart,
                AGUIEventTypes.ReasoningMessageStart,
                AGUIEventTypes.ReasoningMessageContent,
                AGUIEventTypes.ReasoningMessageEnd,
                AGUIEventTypes.ReasoningEnd,
                AGUIEventTypes.TextMessageStart,
                AGUIEventTypes.TextMessageContent,
            ],
            events.Select(evt => evt.Type));
        Assert.Equal("Thinking it over.", Assert.Single(events.OfType<ReasoningMessageContentEvent>()).Delta);
    }

    [Fact]
    public async Task ErrorAsync_EndsTheRunAndDropsLaterEvents()
    {
        var events = new List<BaseEvent>();
        var writer = CreateWriter(events);

        await writer.WriteTextAsync("m1", "Partial", TestContext.Current.CancellationToken);
        await writer.ErrorAsync("It failed.", "boom", TestContext.Current.CancellationToken);
        await writer.WriteTextAsync("m1", "Ignored", TestContext.Current.CancellationToken);
        await writer.FinishAsync(TestContext.Current.CancellationToken);

        var error = Assert.IsType<RunErrorEvent>(events[^1]);
        Assert.Equal("It failed.", error.Message);
        Assert.Equal("boom", error.Code);
        Assert.True(writer.IsFinished);
        Assert.DoesNotContain(events, evt => evt is RunFinishedEvent);
        Assert.Contains(events, evt => evt is TextMessageEndEvent);
    }

    [Fact]
    public async Task WriteStateDeltaAsync_WritesTheJsonPatchOperations()
    {
        var events = new List<BaseEvent>();
        var writer = CreateWriter(events);

        await writer.WriteStateSnapshotAsync(new JsonObject { ["title"] = "Draft" }, TestContext.Current.CancellationToken);
        await writer.WriteStateDeltaAsync(new JsonArray(new JsonObject { ["op"] = "replace", ["path"] = "/title", ["value"] = "Final" }), TestContext.Current.CancellationToken);

        Assert.Equal("Draft", Assert.Single(events.OfType<StateSnapshotEvent>()).Snapshot.GetProperty("title").GetString());
        Assert.Equal("/title", Assert.Single(events.OfType<StateDeltaEvent>()).Delta[0].GetProperty("path").GetString());
    }

    [Fact]
    public async Task SerializeEvent_WritesTheAGUIWireFormat()
    {
        var events = new List<BaseEvent>();
        var writer = CreateWriter(events);

        await writer.WriteTextAsync("m1", "Hi", TestContext.Current.CancellationToken);

        var json = AGUIJson.SerializeEvent(events.OfType<TextMessageContentEvent>().Single());

        Assert.Equal("TEXT_MESSAGE_CONTENT", json.GetProperty("type").GetString());
        Assert.Equal("m1", json.GetProperty("messageId").GetString());
        Assert.Equal("Hi", json.GetProperty("delta").GetString());
        Assert.False(json.TryGetProperty("rawEvent", out _));
    }

    [Fact]
    public void DeserializeInput_ReadsTheRunInput()
    {
        var input = AGUIJson.DeserializeInput(JsonElement.Parse("""
            {
              "threadId": "t1",
              "runId": "r1",
              "messages": [{ "id": "u1", "role": "user", "content": "Hello" }],
              "tools": [],
              "context": [],
              "forwardedProps": { "profileId": "p1" }
            }
            """));

        Assert.Equal("t1", input.ThreadId);
        Assert.IsType<AGUIUserMessage>(Assert.Single(input.Messages));
    }

    private static AGUIRunEventWriter CreateWriter(List<BaseEvent> events)
    {
        return new AGUIRunEventWriter("thread-1", "run-1", Collect(events));
    }

    private static Func<BaseEvent, CancellationToken, ValueTask> Collect(List<BaseEvent> events)
    {
        return (evt, _) =>
        {
            events.Add(evt);

            return ValueTask.CompletedTask;
        };
    }
}
