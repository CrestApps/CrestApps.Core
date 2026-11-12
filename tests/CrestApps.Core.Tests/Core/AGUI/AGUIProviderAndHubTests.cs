using AGUI.Abstractions;
using CrestApps.Core.AI.AGUI;
using CrestApps.Core.AI.Chat.AGUI;
using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.Chat.Models;
using CrestApps.Core.AI.OpenAI.Azure.Services;
using Microsoft.Extensions.AI;
using Moq;
using OpenAI.Chat;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AIChatRole = Microsoft.Extensions.AI.ChatRole;

namespace CrestApps.Core.Tests.Core.AGUI;

public sealed class AGUIProviderAndHubTests
{
    [Fact]
    public void AzureConverter_ConvertsToolCallsAndToolResults()
    {
        var converted = AzureOpenAIChatMessageConverter.Convert(
        [
            new AIChatMessage(AIChatRole.User, "Where is A-1?"),
            new AIChatMessage(AIChatRole.Assistant, [new FunctionCallContent("c1", "get_order", new Dictionary<string, object> { ["id"] = "A-1" })]),
            new AIChatMessage(AIChatRole.Tool, [new FunctionResultContent("c1", "shipped")]),
            new AIChatMessage(AIChatRole.Assistant, "It shipped."),
        ],
        pastMessagesCount: null);

        Assert.Equal(4, converted.Count);

        var assistant = Assert.IsType<AssistantChatMessage>(converted[1]);
        var toolCall = Assert.Single(assistant.ToolCalls);
        Assert.Equal("c1", toolCall.Id);
        Assert.Equal("get_order", toolCall.FunctionName);
        Assert.Contains("A-1", toolCall.FunctionArguments.ToString());

        var tool = Assert.IsType<ToolChatMessage>(converted[2]);
        Assert.Equal("c1", tool.ToolCallId);
        Assert.Equal("shipped", Assert.Single(tool.Content).Text);
    }

    [Fact]
    public void AzureConverter_DropsToolResultsWhoseCallsWereCutOff()
    {
        var converted = AzureOpenAIChatMessageConverter.Convert(
        [
            new AIChatMessage(AIChatRole.User, "Where is A-1?"),
            new AIChatMessage(AIChatRole.Assistant, [new FunctionCallContent("c1", "get_order")]),
            new AIChatMessage(AIChatRole.Tool, [new FunctionResultContent("c1", "shipped"), new FunctionResultContent("c2", "late")]),
            new AIChatMessage(AIChatRole.Assistant, "It shipped."),
        ],
        pastMessagesCount: 2);

        // The newest two SDK messages are the second tool result and the answer; the result lost its call.
        var answer = Assert.Single(converted);
        Assert.IsType<AssistantChatMessage>(answer);
    }

    [Fact]
    public void ToFunctionCallContent_ReadsTheArguments()
    {
        var call = ChatToolCall.CreateFunctionToolCall("c1", "get_order", BinaryData.FromString("""{"id":"A-1"}"""));

        var content = AzureOpenAICompletionClient.ToFunctionCallContent(call);

        Assert.Equal("c1", content.CallId);
        Assert.Equal("get_order", content.Name);
        Assert.Equal("A-1", content.Arguments["id"]?.ToString());
    }

    [Fact]
    public async Task AGUIChatTurn_WritesPartialMessagesAsTextAndReplacesStreamedText()
    {
        var events = new List<BaseEvent>();
        var turn = CreateTurn(events);

        await turn.WriteAsync(new CompletionPartialMessage { SessionId = "s1", MessageId = "m1", Content = "Hello" }, TestContext.Current.CancellationToken);
        await turn.WriteAsync(new CompletionPartialMessage { SessionId = "s1", MessageId = "m1", Content = "Blocked." }, TestContext.Current.CancellationToken);

        Assert.Equal("s1", turn.Events.ThreadId);
        Assert.Equal("Hello", Assert.Single(events.OfType<TextMessageContentEvent>()).Delta);

        var replaced = Assert.Single(events.OfType<CustomEvent>(), evt => evt.Name == AGUIChatTurn.MessageReplacedEventName);
        Assert.Equal("Blocked.", replaced.Value.Value.GetProperty("content").GetString());
        Assert.True(turn.TryComplete());
    }

    [Fact]
    public async Task AGUIChatTurn_InitializeSharedStateAsync_SendsTheSnapshotAndEachChange()
    {
        var events = new List<BaseEvent>();
        var turn = CreateTurn(events, """{ "threadId": "t", "runId": "r", "messages": [], "tools": [], "context": [], "state": { "note": "" }, "forwardedProps": {} }""");

        var state = await turn.InitializeSharedStateAsync(TestContext.Current.CancellationToken);
        await state.OnChangedAsync(new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["op"] = "replace", ["path"] = "/note", ["value"] = "x" }), state.Value, TestContext.Current.CancellationToken);

        Assert.Single(events.OfType<StateSnapshotEvent>());
        Assert.Single(events.OfType<StateDeltaEvent>());
    }

    [Fact]
    public async Task AGUIHubClientProxy_TurnsTheCallersErrorsIntoTheRunsError()
    {
        var events = new List<BaseEvent>();
        var turn = CreateTurn(events);
        var inner = new Mock<IAIChatHubClient>();
        var client = AGUIHubClientProxy.Create(inner.Object, turn);

        await client.ReceiveError("Not allowed.");
        await client.MessageRated("m1", true);

        var error = Assert.IsType<RunErrorEvent>(events[^1]);
        Assert.Equal("Not allowed.", error.Message);
        inner.Verify(candidate => candidate.ReceiveError(It.IsAny<string>()), Times.Never);
        inner.Verify(candidate => candidate.MessageRated("m1", true), Times.Once);
    }

    [Fact]
    public async Task AGUIHubClientProxy_MarksASessionStartRejection()
    {
        var events = new List<BaseEvent>();
        var client = AGUIHubClientProxy.Create<IAIChatHubClient>(inner: null, CreateTurn(events));

        await client.ReceiveSessionStartRejected("Too many chats.");

        Assert.Equal("session_start_rejected", Assert.IsType<RunErrorEvent>(events[^1]).Code);
    }

    private static AGUIChatTurn CreateTurn(List<BaseEvent> events, string input = null)
    {
        var request = AGUIRunRequest.From(AGUIJson.DeserializeInput(System.Text.Json.JsonElement.Parse(
            input ?? """{ "threadId": "", "runId": "r", "messages": [], "tools": [], "context": [], "forwardedProps": {} }""")));
        var writer = new AGUIRunEventWriter(request.ThreadId, request.RunId, (evt, _) =>
        {
            events.Add(evt);

            return ValueTask.CompletedTask;
        });

        return new AGUIChatTurn(request, writer);
    }
}
