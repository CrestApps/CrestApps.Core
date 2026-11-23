using System.Text.Json;
using AGUI.Abstractions;
using CrestApps.Core.AI.AGUI;
using CrestApps.Core.AI.Chat.Hubs;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001 // Tool approvals are a preview API of Microsoft.Extensions.AI.
namespace CrestApps.Core.Tests.Core.Orchestration;

/// <summary>
/// Covers a tool that requires a person's approval when it sits inside an agent the assistant delegates to. The
/// runs go through the real pipeline (see <see cref="AgentApprovalTestHarness"/>), and a turn is stored and
/// resumed the way the chat hub does it: the AG-UI event writer keeps the tool calls, the paused message is
/// stored, and <see cref="ChatTurnResume"/> applies the person's answers.
/// </summary>
public sealed class AgentToolApprovalTests
{
    private const string AgentName = AgentApprovalTestHarness.AgentName;
    private const string AgentCallId = AgentApprovalTestHarness.AgentCallId;
    private const string DeleteToolName = AgentApprovalTestHarness.DeleteItemTool.TheName;

    [Fact]
    public async Task AToolThatRequiresApproval_InsideAnAgent_DoesNotRunBeforeAPersonApproves()
    {
        using var harness = new AgentApprovalTestHarness();
        using var scope = AIInvocationScope.Begin();

        var updates = await harness.RunAssistantAsync(
            scope.Context,
            [new ChatMessage(ChatRole.User, AgentApprovalTestHarness.Prompt)],
            supportsApproval: true);

        // The agent listed the items, which needs no approval, and then asked to delete one.
        Assert.Equal(1, harness.ListTool.CallCount);
        Assert.Equal(0, harness.DeleteTool.CallCount);
        Assert.Contains(updates.SelectMany(update => update.Contents), content => content is ToolApprovalRequestContent);
    }

    [Fact]
    public async Task AnAgentSetToRunWithoutAsking_RunsItsToolWithoutAnApproval_EvenWhenThePersonCouldBeAsked()
    {
        using var harness = new AgentApprovalTestHarness();
        using var scope = AIInvocationScope.Begin();

        harness.AgentContexts.RunsToolsWithoutAsking = true;

        var updates = await harness.RunAssistantAsync(
            scope.Context,
            [new ChatMessage(ChatRole.User, AgentApprovalTestHarness.Prompt)],
            supportsApproval: true);

        Assert.Equal(1, harness.DeleteTool.CallCount);
        Assert.DoesNotContain(updates.SelectMany(update => update.Contents), content => content is ToolApprovalRequestContent);
    }

    [Fact]
    public async Task AnAssistantSetToRunWithoutAsking_DoesNotPassItsChoiceToTheAgent()
    {
        using var harness = new AgentApprovalTestHarness { AssistantRunsToolsWithoutAsking = true };
        using var scope = AIInvocationScope.Begin();

        var updates = await harness.RunAssistantAsync(
            scope.Context,
            [new ChatMessage(ChatRole.User, AgentApprovalTestHarness.Prompt)],
            supportsApproval: true);

        // The agent asks, as its own profile says: the assistant's choice is for the assistant's own tools.
        Assert.False(Assert.Single(harness.AgentContexts.Built).CompletionContext.RunsToolsWithoutApproval());
        Assert.Equal(0, harness.DeleteTool.CallCount);
        Assert.Contains(updates.SelectMany(update => update.Contents), content => content is ToolApprovalRequestContent);
    }

    [Fact]
    public async Task TheAssistantsRunPauses_WithARequestThatNamesTheAgentTheToolAndItsArguments()
    {
        using var harness = new AgentApprovalTestHarness();
        var paused = await RunFirstTurnAsync(harness);

        // The agent's run inherited that its caller can ask.
        var agentContext = Assert.Single(harness.AgentContexts.Built).CompletionContext;
        Assert.Equal(true, agentContext.AdditionalProperties[AICompletionContextKeys.ToolApprovalSupported]);

        // The turn ended after the agent paused: the assistant's model was asked once, and wrote no answer.
        Assert.Single(harness.Model.AssistantRequests);
        Assert.Equal(2, harness.Model.AgentRequests.Count);
        Assert.DoesNotContain(paused.Updates, update => !string.IsNullOrEmpty(update.Text));

        // The call to the agent has no result yet; the agent answers it when it resumes.
        Assert.DoesNotContain(paused.Updates.SelectMany(update => update.Contents), content => content is FunctionResultContent);
        Assert.DoesNotContain(paused.Events, evt => evt is ToolCallResultEvent);

        var outcome = Assert.IsType<RunFinishedInterruptOutcome>(Assert.IsType<RunFinishedEvent>(paused.Events[^1]).Outcome);
        var interrupt = Assert.Single(outcome.Interrupts);
        var metadata = interrupt.Metadata.Value;

        Assert.Equal($"Allow the agent '{AgentName}' to run '{DeleteToolName}'?", interrupt.Message);
        Assert.Equal(AgentName, metadata.GetProperty("agentName").GetString());
        Assert.Equal(AgentCallId, metadata.GetProperty("agentToolCallId").GetString());
        Assert.Equal(DeleteToolName, metadata.GetProperty("toolName").GetString());
        Assert.Equal(7, metadata.GetProperty("arguments").GetProperty("id").GetInt32());

        // The person sees two calls: the agent, which waits, and the agent's tool call with its arguments.
        Assert.Equal(2, paused.Activity.ToolCalls.Count);

        var agentCall = paused.Activity.ToolCalls[0];
        Assert.Equal(AgentCallId, agentCall.CallId);
        Assert.Equal(AgentName, agentCall.Name);
        Assert.Equal(ChatTurnToolCallStatus.AwaitingApproval, agentCall.Status);
        Assert.Null(agentCall.Result);
        Assert.Null(agentCall.ApprovalRequestId);

        var toolCall = paused.Activity.ToolCalls[1];
        Assert.Equal(interrupt.ToolCallId, toolCall.CallId);
        Assert.Equal(interrupt.Id, toolCall.ApprovalRequestId);
        Assert.Equal(DeleteToolName, toolCall.Name);
        Assert.Equal("""{"id":7}""", toolCall.Arguments);
        Assert.Equal(ChatTurnToolCallStatus.AwaitingApproval, toolCall.Status);
        Assert.Equal(AgentName, toolCall.AgentName);
        Assert.Equal(AgentCallId, toolCall.AgentCallId);
        Assert.Contains(paused.Events, evt => evt is ToolCallStartEvent start && start.ToolCallId == toolCall.CallId && start.ToolCallName == DeleteToolName);

        // The agent's run is kept with the message, with what it already did.
        var run = Assert.Single(paused.Activity.AgentRuns);
        Assert.Equal(AgentCallId, run.CallId);
        Assert.Equal(AgentName, run.AgentName);
        Assert.Equal(
            [(AgentApprovalTestHarness.ListCallId, ChatTurnToolCallStatus.Completed), (AgentApprovalTestHarness.DeleteCallId, ChatTurnToolCallStatus.AwaitingApproval)],
            run.ToolCalls.Select(call => (call.CallId, call.Status)));
        Assert.Equal("Items: 7, 8", run.ToolCalls[0].Result);
        Assert.True(paused.Activity.HasPendingCalls);
    }

    [Fact]
    public async Task Approving_ContinuesTheAgentWhereItStopped_AndRunsNothingTwice()
    {
        using var harness = new AgentApprovalTestHarness();
        var paused = await RunFirstTurnAsync(harness);
        var resumed = await ResumeAsync(harness, paused, approved: true);

        // The approved call ran once, and the call the agent made before the pause was not made again.
        Assert.Equal(1, harness.DeleteTool.CallCount);
        Assert.Equal(1, harness.ListTool.CallCount);

        // The agent was not started again: its model was asked once more, with its earlier work in the conversation.
        Assert.Equal(3, harness.Model.AgentRequests.Count);

        var agentMessages = harness.Model.AgentRequests[2];
        Assert.Equal(AgentApprovalTestHarness.Prompt, agentMessages.First(message => message.Role == ChatRole.User).Text);
        Assert.Equal("Items: 7, 8", FindResult(agentMessages, AgentApprovalTestHarness.ListCallId));
        Assert.Equal("Deleted item 7.", FindResult(agentMessages, AgentApprovalTestHarness.DeleteCallId));

        // The assistant's model was asked once more, with the agent's answer as the result of its call.
        Assert.Equal(2, harness.Model.AssistantRequests.Count);
        Assert.Equal("The delete tool answered: Deleted item 7.", FindResult(harness.Model.AssistantRequests[1], AgentCallId));
        Assert.Equal("The agent answered: The delete tool answered: Deleted item 7.", resumed.Text);

        // The paused message shows what the person decided, and keeps nothing to resume again.
        Assert.All(paused.Activity.ToolCalls, call => Assert.Equal(ChatTurnToolCallStatus.Approved, call.Status));
        Assert.Empty(paused.Activity.AgentRuns);
        Assert.False(paused.Activity.HasPendingCalls);

        // The resumed message has the call to the agent, completed, and nothing waiting.
        var agentCall = Assert.Single(resumed.Activity.ToolCalls);
        Assert.Equal(AgentCallId, agentCall.CallId);
        Assert.Equal(ChatTurnToolCallStatus.Completed, agentCall.Status);
        Assert.Equal("The delete tool answered: Deleted item 7.", agentCall.Result);
        Assert.Empty(resumed.Activity.AgentRuns);
        Assert.IsType<RunFinishedSuccessOutcome>(Assert.IsType<RunFinishedEvent>(resumed.Events[^1]).Outcome);
    }

    [Fact]
    public async Task Rejecting_GivesTheAgentARefusalItReports_AndTheToolNeverRuns()
    {
        using var harness = new AgentApprovalTestHarness();
        var paused = await RunFirstTurnAsync(harness);
        var resumed = await ResumeAsync(harness, paused, approved: false);

        Assert.Equal(0, harness.DeleteTool.CallCount);
        Assert.Equal(1, harness.ListTool.CallCount);

        // The agent's model read that the call was rejected, and the assistant got the agent's report.
        Assert.Equal(3, harness.Model.AgentRequests.Count);

        var refusal = FindResult(harness.Model.AgentRequests[2], AgentApprovalTestHarness.DeleteCallId);
        Assert.Contains("rejected", refusal, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"The agent answered: The delete tool answered: {refusal}", resumed.Text);

        // The call to the agent was made again so the agent could report; the tool call shows as rejected.
        Assert.Equal(ChatTurnToolCallStatus.Approved, paused.Activity.ToolCalls[0].Status);
        Assert.Equal(ChatTurnToolCallStatus.Rejected, paused.Activity.ToolCalls[1].Status);
        Assert.Equal(ChatTurnToolCallStatus.Completed, Assert.Single(resumed.Activity.ToolCalls).Status);
    }

    [Fact]
    public async Task AnApprovalTheClientDidNotAnswer_CountsAsRejected()
    {
        using var harness = new AgentApprovalTestHarness();
        var paused = await RunFirstTurnAsync(harness);
        var resumed = await ResumeAsync(harness, paused, approved: null);

        Assert.Equal(0, harness.DeleteTool.CallCount);
        Assert.Contains("rejected", resumed.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ASecondToolThatRequiresApproval_PausesTheResumedRunAgain_AndKeepsWhatTheAgentDid()
    {
        using var harness = new AgentApprovalTestHarness();
        harness.Model.DeletesTwoItems = true;

        var paused = await RunFirstTurnAsync(harness);
        var pausedAgain = await ResumeAsync(harness, paused, approved: true);

        // The first delete ran; the second one waits for an approval of its own.
        Assert.Equal(1, harness.DeleteTool.CallCount);
        Assert.Single(harness.Model.AssistantRequests);

        var interrupt = Assert.Single(Assert.IsType<RunFinishedInterruptOutcome>(Assert.IsType<RunFinishedEvent>(pausedAgain.Events[^1]).Outcome).Interrupts);
        Assert.Equal(8, interrupt.Metadata.Value.GetProperty("arguments").GetProperty("id").GetInt32());

        var run = Assert.Single(pausedAgain.Activity.AgentRuns);
        Assert.Equal(
            [ChatTurnToolCallStatus.Completed, ChatTurnToolCallStatus.Completed, ChatTurnToolCallStatus.AwaitingApproval],
            run.ToolCalls.Select(call => call.Status));

        var finished = await ResumeAsync(harness, pausedAgain, approved: true, history: paused.History);

        Assert.Equal(2, harness.DeleteTool.CallCount);
        Assert.Equal(1, harness.ListTool.CallCount);
        Assert.Equal("The agent answered: The delete tool answered: Deleted item 7. Then: Deleted item 8.", finished.Text);

        // Every call the agent made is in the conversation its model continued from, once.
        var agentMessages = harness.Model.AgentRequests[^1];
        Assert.Equal("Items: 7, 8", FindResult(agentMessages, AgentApprovalTestHarness.ListCallId));
        Assert.Equal("Deleted item 7.", FindResult(agentMessages, AgentApprovalTestHarness.DeleteCallId));
        Assert.Equal("Deleted item 8.", FindResult(agentMessages, AgentApprovalTestHarness.SecondDeleteCallId));
        Assert.Equal(3, agentMessages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Count());
    }

    [Fact]
    public async Task ACallerThatCannotAsk_DoesNotRunTheTool_AndTheAgentIsToldWhy()
    {
        using var harness = new AgentApprovalTestHarness();
        using var scope = AIInvocationScope.Begin();

        var updates = await harness.RunAssistantAsync(
            scope.Context,
            [new ChatMessage(ChatRole.User, AgentApprovalTestHarness.Prompt)],
            supportsApproval: false);

        Assert.Equal(0, harness.DeleteTool.CallCount);
        Assert.Equal(1, harness.ListTool.CallCount);
        Assert.DoesNotContain(updates.SelectMany(update => update.Contents), content => content is ToolApprovalRequestContent);

        var agentContext = Assert.Single(harness.AgentContexts.Built).CompletionContext;
        Assert.False(agentContext.AdditionalProperties.ContainsKey(AICompletionContextKeys.ToolApprovalSupported));

        // The agent's model read why the tool did not run, and the assistant got the agent's report.
        var told = FindResult(harness.Model.AgentRequests[^1], AgentApprovalTestHarness.DeleteCallId);
        Assert.Equal(ApprovalUnavailableAIFunction.GetMessage(DeleteToolName), told);
        Assert.Equal($"The agent answered: The delete tool answered: {told}", string.Concat(updates.Select(update => update.Text)));
    }

    [Fact]
    public async Task AnAgentCalledWithoutAnInvocationOfTheAssistantsTurn_CannotAsk()
    {
        using var harness = new AgentApprovalTestHarness();
        using var scope = AIInvocationScope.Begin();

        // An agent that is called directly, as the MCP server does, has no turn that could show a request.
        scope.Context.CompletionContext = new AICompletionContext { ChatDeploymentName = "chat" };

        var tool = new AgentProxyTool(AgentName, "Lists and deletes items.");
        var result = await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object> { ["prompt"] = AgentApprovalTestHarness.Prompt }) { Services = harness.Services },
            TestContext.Current.CancellationToken);

        Assert.Equal(0, harness.DeleteTool.CallCount);
        Assert.Equal($"The delete tool answered: {ApprovalUnavailableAIFunction.GetMessage(DeleteToolName)}", result?.ToString());
    }

    [Fact]
    public async Task TheMaterializer_ReplacesAToolThatRequiresApproval_WhenTheApprovalCannotBeAsked()
    {
        var calls = 0;
        var function = AIFunctionFactory.Create(() => ++calls, "delete_item");
        var entry = new ToolRegistryEntry { Name = "delete_item", RequiresApproval = true };

        // A caller that can ask pauses for the approval; a tool that requires none is offered as it is.
        Assert.IsType<ApprovalRequiredAIFunction>(DefaultToolMaterializer.WrapForApproval(function, entry, new ToolMaterializationOptions { SupportsToolApproval = true }));
        Assert.Same(function, DefaultToolMaterializer.WrapForApproval(function, new ToolRegistryEntry { Name = "read" }, new ToolMaterializationOptions()));

        // A caller that cannot ask, inside an agent or not, never runs it.
        var refusing = Assert.IsType<ApprovalUnavailableAIFunction>(DefaultToolMaterializer.WrapForApproval(function, entry, new ToolMaterializationOptions()));

        Assert.Equal("delete_item", refusing.Name);
        Assert.Equal(function.JsonSchema.GetRawText(), refusing.JsonSchema.GetRawText());
        Assert.Same(refusing, DefaultToolMaterializer.WrapForApproval(refusing, entry, new ToolMaterializationOptions()));

        var result = await refusing.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        Assert.Equal(0, calls);
        Assert.Equal(ApprovalUnavailableAIFunction.GetMessage("delete_item"), result?.ToString());
    }

    [Fact]
    public void TheAnswersToAnAssistantsOwnCalls_AreAppliedAsBefore()
    {
        var activity = new ChatTurnToolActivity
        {
            ToolCalls =
            [
                new ChatTurnToolCall { CallId = "c1", Name = "delete_item", Arguments = """{"id":1}""", Status = ChatTurnToolCallStatus.AwaitingApproval, ApprovalRequestId = "r1" },
                new ChatTurnToolCall { CallId = "c2", Name = "send_mail", Arguments = "{}", Status = ChatTurnToolCallStatus.AwaitingApproval, ApprovalRequestId = "r2" },
                new ChatTurnToolCall { CallId = "c3", Name = "get_weather", Arguments = "{}", Status = ChatTurnToolCallStatus.AwaitingClient },
                new ChatTurnToolCall { CallId = "c4", Name = "list_items", Arguments = "{}", Status = ChatTurnToolCallStatus.Completed, Result = "Items" },
            ],
        };
        var history = new List<ChatMessage> { new(ChatRole.User, "Do it") };
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        var request = CreateResumeRequest(new Dictionary<string, bool> { ["r1"] = true, ["r2"] = false }, clientResults: new Dictionary<string, string> { ["c3"] = "Sunny" });
        using var scope = AIInvocationScope.Begin();

        ChatTurnResume.Apply(activity, request, history, rejected, scope.Context);

        Assert.Equal(
            [ChatTurnToolCallStatus.Approved, ChatTurnToolCallStatus.Rejected, ChatTurnToolCallStatus.Completed, ChatTurnToolCallStatus.Completed],
            activity.ToolCalls.Select(call => call.Status));
        Assert.Equal("Sunny", activity.ToolCalls[2].Result);
        Assert.Equal(["c2"], rejected);
        Assert.Null(AgentToolApprovals.Get(scope.Context));

        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant, ChatRole.User], history.Select(message => message.Role));
        Assert.Equal("c3", Assert.IsType<FunctionCallContent>(Assert.Single(history[1].Contents)).CallId);
        Assert.Equal("Sunny", Assert.IsType<FunctionResultContent>(Assert.Single(history[2].Contents)).Result);
        Assert.Equal(["r1", "r2"], history[3].Contents.OfType<ToolApprovalRequestContent>().Select(approval => approval.RequestId));
        Assert.Equal([true, false], history[4].Contents.OfType<ToolApprovalResponseContent>().Select(response => response.Approved));
    }

    [Fact]
    public async Task TheApprovalRequest_NamesTheAgentByItsTitle_AndKeepsItsNameAsTheId()
    {
        var events = new List<BaseEvent>();
        var writer = CreateWriter(events, "run-1");
        var agentCall = new FunctionCallContent(AgentCallId, AgentName, new Dictionary<string, object> { ["prompt"] = "Delete item 7" });
        var run = new ChatTurnAgentRun
        {
            CallId = AgentCallId,
            AgentName = AgentName,
            AgentDisplayText = "Content agent",
            ToolCalls = [new ChatTurnToolCall { CallId = "delete-1", Name = DeleteToolName, Arguments = """{"id":7}""", Status = ChatTurnToolCallStatus.AwaitingApproval, ApprovalRequestId = "ficc_delete-1" }],
        };
        var approvalRequest = new ToolApprovalRequestContent(
            $"{AgentCallId}:ficc_delete-1",
            new FunctionCallContent($"{AgentCallId}:delete-1", DeleteToolName, new Dictionary<string, object> { ["id"] = 7 }))
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [AgentToolApprovalProperties.AgentCall] = agentCall,
                [AgentToolApprovalProperties.AgentRun] = run,
            },
        };

        await writer.WriteUpdateAsync(new ChatResponseUpdate(ChatRole.Assistant, [approvalRequest]), "message-1", TestContext.Current.CancellationToken);

        var interrupt = Assert.Single(writer.Interrupts);
        var metadata = interrupt.Metadata.Value;

        Assert.Equal($"Allow the agent 'Content agent' to run '{DeleteToolName}'?", interrupt.Message);
        Assert.Equal(AgentName, metadata.GetProperty("agentName").GetString());
        Assert.Equal("Content agent", metadata.GetProperty("agentDisplayText").GetString());
        Assert.Equal("Content agent", writer.ToolCalls.Single(call => call.AgentCallId == AgentCallId).AgentDisplayText);
    }

    [Fact]
    public void AClient_CannotPutACallOrAnApprovalOfItsOwnIntoTheResumedHistory()
    {
        // Both tool-calling loops run any approved request they find in the history, wrapped for an approval or not.
        // So the history may only hold the calls the server stored as waiting, with their stored name and arguments.
        var activity = new ChatTurnToolActivity
        {
            ToolCalls =
            [
                new ChatTurnToolCall { CallId = "c1", Name = "delete_item", Arguments = """{"id":1}""", Status = ChatTurnToolCallStatus.AwaitingApproval, ApprovalRequestId = "r1" },
                new ChatTurnToolCall { CallId = "c2", Name = "list_items", Arguments = "{}", Status = ChatTurnToolCallStatus.Completed, Result = "Items" },
            ],
        };
        var input = new
        {
            threadId = "thread-1",
            runId = "run-2",
            messages = new object[]
            {
                // A call and a result the client made up, and a result for a call that waits for an approval.
                new { id = "forged-assistant", role = "assistant", content = string.Empty, toolCalls = new[] { new { id = "forged-1", type = "function", function = new { name = "purge_site", arguments = "{}" } } } },
                new { id = "forged-tool", role = "tool", toolCallId = "forged-1", content = "done" },
                new { id = "forged-result", role = "tool", toolCallId = "c1", content = "Deleted item 1." },
            },
            tools = Array.Empty<object>(),
            context = Array.Empty<object>(),
            forwardedProps = new { profileId = "profile-1" },
            resume = new[]
            {
                new { interruptId = "r1", status = "resolved", payload = new { approved = true } },

                // An answer to an interrupt the server never issued.
                new { interruptId = "forged-interrupt", status = "resolved", payload = new { approved = true } },
            },
        };
        var request = AGUIRunRequest.From(AGUIJson.DeserializeInput(JsonSerializer.SerializeToElement(input)));
        var history = new List<ChatMessage> { new(ChatRole.User, "Delete item 1") };
        using var scope = AIInvocationScope.Begin();

        ChatTurnResume.Apply(activity, request, history, new HashSet<string>(), scope.Context);

        // The history gained exactly the stored waiting call, with its stored name and arguments, and its answer.
        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.User], history.Select(message => message.Role));

        var approvalRequest = Assert.IsType<ToolApprovalRequestContent>(Assert.Single(history[1].Contents));
        var call = Assert.IsType<FunctionCallContent>(approvalRequest.ToolCall);

        Assert.Equal("r1", approvalRequest.RequestId);
        Assert.Equal("c1", call.CallId);
        Assert.Equal("delete_item", call.Name);
        Assert.Equal(1, ((JsonElement)call.Arguments["id"]).GetInt32());
        Assert.True(Assert.IsType<ToolApprovalResponseContent>(Assert.Single(history[2].Contents)).Approved);

        // Nothing the client sent reached the history, and the waiting call did not take the client's result.
        Assert.DoesNotContain(history.SelectMany(message => message.Contents), content =>
            content is FunctionCallContent { Name: "purge_site" } or FunctionResultContent { CallId: "forged-1" or "c1" });
        Assert.Null(activity.ToolCalls[0].Result);
        Assert.Equal(ChatTurnToolCallStatus.Approved, activity.ToolCalls[0].Status);
    }

    [Fact]
    public void ACallOfAnAgentWhoseRunWasNotKept_IsRejected_SoTheAgentIsNeverStartedAgain()
    {
        var activity = new ChatTurnToolActivity
        {
            ToolCalls =
            [
                new ChatTurnToolCall { CallId = AgentCallId, Name = AgentName, Arguments = """{"prompt":"Delete item 7"}""", Status = ChatTurnToolCallStatus.AwaitingApproval },
                new ChatTurnToolCall { CallId = $"{AgentCallId}:delete-1", Name = DeleteToolName, Arguments = """{"id":7}""", Status = ChatTurnToolCallStatus.AwaitingApproval, ApprovalRequestId = $"{AgentCallId}:ficc_delete-1", AgentName = AgentName, AgentCallId = AgentCallId },
            ],
        };
        var history = new List<ChatMessage>();
        var request = CreateResumeRequest(new Dictionary<string, bool> { [$"{AgentCallId}:ficc_delete-1"] = true });
        using var scope = AIInvocationScope.Begin();

        ChatTurnResume.Apply(activity, request, history, new HashSet<string>(), scope.Context);

        // Without the agent's run there is nothing to continue from, and starting the agent again would repeat
        // what it did. The call to the agent is answered as rejected.
        var response = Assert.IsType<ToolApprovalResponseContent>(Assert.Single(history[1].Contents));

        Assert.False(response.Approved);
        Assert.Equal(AgentCallId, Assert.IsType<FunctionCallContent>(response.ToolCall).CallId);
        Assert.Null(AgentToolApprovals.Get(scope.Context));
    }

    [Fact]
    public void TheAgentsRun_BecomesTheConversationTheAgentContinuesFrom()
    {
        var run = new ChatTurnAgentRun { CallId = AgentCallId, AgentName = AgentName };

        AgentRunHistory.Record(run, new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("list-1", "list_items", new Dictionary<string, object>())]));
        AgentRunHistory.Record(run, new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("list-1", new string('x', ChatTurnAgentRun.MaxToolResultLength + 10))]));
        AgentRunHistory.Record(run, new ChatResponseUpdate(ChatRole.Assistant,
        [
            new ToolApprovalRequestContent("ficc_delete-1", new FunctionCallContent("delete-1", "delete_item", new Dictionary<string, object> { ["id"] = 7 })),
            new ToolApprovalRequestContent("ficc_delete-2", new FunctionCallContent("delete-2", "delete_item", new Dictionary<string, object> { ["id"] = 8 })),
        ]));

        Assert.True(run.HasPendingCalls);
        Assert.Equal(ChatTurnAgentRun.MaxToolResultLength + 1, run.ToolCalls[0].Result.Length);

        var history = AgentRunHistory.BuildResumeHistory(
            "Delete items 7 and 8",
            new AgentRunResumption(run, new Dictionary<string, bool> { [AgentToolApprovals.ComposeId(AgentCallId, "ficc_delete-1")] = true }));

        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant, ChatRole.User], history.Select(message => message.Role));
        Assert.Equal("Delete items 7 and 8", history[0].Text);
        Assert.Equal("list-1", Assert.IsType<FunctionCallContent>(Assert.Single(history[1].Contents)).CallId);
        Assert.Equal("list-1", Assert.IsType<FunctionResultContent>(Assert.Single(history[2].Contents)).CallId);
        Assert.Equal(["ficc_delete-1", "ficc_delete-2"], history[3].Contents.OfType<ToolApprovalRequestContent>().Select(approval => approval.RequestId));
        Assert.Equal([true, false], history[4].Contents.OfType<ToolApprovalResponseContent>().Select(response => response.Approved));
        Assert.True(DefaultOrchestrator.IsApprovalResume(history));
        Assert.Equal(
            [ChatTurnToolCallStatus.Completed, ChatTurnToolCallStatus.Approved, ChatTurnToolCallStatus.Rejected],
            run.ToolCalls.Select(call => call.Status));
        Assert.False(run.HasPendingCalls);

        // The loop answers the approved call with its result and the rejected one with the rejection.
        AgentRunHistory.Record(run, new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("delete-1", "Deleted item 7."), new FunctionResultContent("delete-2", "Rejected.")]));

        Assert.Equal(
            [ChatTurnToolCallStatus.Completed, ChatTurnToolCallStatus.Completed, ChatTurnToolCallStatus.Rejected],
            run.ToolCalls.Select(call => call.Status));
    }

    [Fact]
    public void AnAgentsPause_EndsOnlyTheAssistantsTurn()
    {
        using var scope = AIInvocationScope.Begin();
        var approvals = AgentToolApprovals.GetOrAdd(scope.Context);
        var run = new ChatTurnAgentRun
        {
            CallId = AgentCallId,
            AgentName = AgentName,
            ToolCalls = [new ChatTurnToolCall { CallId = "delete-1", Name = DeleteToolName, Arguments = "{}", Status = ChatTurnToolCallStatus.AwaitingApproval, ApprovalRequestId = "ficc_delete-1" }],
        };

        Assert.False(AgentToolApprovals.IsTurnPaused(scope.Context));
        Assert.False(AgentToolApprovals.IsTurnPaused(null));

        approvals.Pause(new FunctionCallContent(AgentCallId, AgentName, new Dictionary<string, object> { ["prompt"] = "Delete item 7" }), run);

        Assert.True(AgentToolApprovals.IsTurnPaused(scope.Context));

        // Another agent that the same round of tool calls started is not cut short.
        scope.Context.AgentInvocationDepth = 1;
        Assert.False(AgentToolApprovals.IsTurnPaused(scope.Context));
        scope.Context.AgentInvocationDepth = 0;

        // The result of the paused call is taken out of the stream; other results stay.
        var update = new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent(AgentCallId, "paused"), new FunctionResultContent("other", "kept")]);
        var kept = Assert.IsType<FunctionResultContent>(Assert.Single(approvals.RemovePausedResults(update).Contents));

        Assert.Equal("other", kept.CallId);
        Assert.Null(approvals.RemovePausedResults(new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent(AgentCallId, "paused")])));

        var request = Assert.IsType<ToolApprovalRequestContent>(Assert.Single(Assert.Single(approvals.TakeApprovalRequests()).Contents));

        Assert.Equal($"{AgentCallId}:ficc_delete-1", request.RequestId);
        Assert.Equal($"{AgentCallId}:delete-1", Assert.IsType<FunctionCallContent>(request.ToolCall).CallId);
        Assert.Same(run, request.AdditionalProperties[AgentToolApprovalProperties.AgentRun]);
        Assert.Empty(approvals.TakeApprovalRequests());

        // Once the turn's requests are taken, the pause is over: a later completion in the same invocation is neither
        // ended early nor stripped of its results.
        Assert.False(AgentToolApprovals.IsTurnPaused(scope.Context));

        var later = new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent(AgentCallId, "a later result")]);
        Assert.Same(later, approvals.RemovePausedResults(later));
    }

    [Fact]
    public void TheToolLoop_EndsOnlyTheAssistantsTurn_WhenAToolAsksOrAnAgentPaused()
    {
        Assert.False(AIToolLoop.ShouldEndTurn(null));

        using var scope = AIInvocationScope.Begin();

        Assert.False(AIToolLoop.ShouldEndTurn(scope.Context));

        // A tool ended the turn, for example one that opened another page.
        scope.Context.Items[AICompletionContextKeys.EndTurnAfterToolCalls] = true;
        Assert.True(AIToolLoop.ShouldEndTurn(scope.Context));

        // An agent that the same round of tool calls started is not cut short.
        scope.Context.AgentInvocationDepth = 1;
        Assert.False(AIToolLoop.ShouldEndTurn(scope.Context));
        scope.Context.AgentInvocationDepth = 0;
        scope.Context.Items.Remove(AICompletionContextKeys.EndTurnAfterToolCalls);

        // An agent paused for an approval, until the turn's approval requests are taken.
        var approvals = AgentToolApprovals.GetOrAdd(scope.Context);

        approvals.Pause(
            new FunctionCallContent(AgentCallId, AgentName),
            new ChatTurnAgentRun { CallId = AgentCallId, AgentName = AgentName });

        Assert.True(AIToolLoop.ShouldEndTurn(scope.Context));

        approvals.TakeApprovalRequests();

        Assert.False(AIToolLoop.ShouldEndTurn(scope.Context));
    }

    [Fact]
    public async Task AResumeThatCannotAskAgain_ContinuesTheKeptRun_InsteadOfStartingTheAgentOver()
    {
        using var harness = new AgentApprovalTestHarness();
        var paused = await RunFirstTurnAsync(harness);

        await ResumeAsync(harness, paused, approved: true, supportsApproval: false);

        // The call the agent made before the pause was not made again, and the approved tool, which nobody can be
        // asked for now, did not run.
        Assert.Equal(1, harness.ListTool.CallCount);
        Assert.Equal(0, harness.DeleteTool.CallCount);

        // The agent's model read its earlier call from the kept run, and why the approved tool was not run.
        var agentMessages = harness.Model.AgentRequests[^1];

        Assert.Equal(3, harness.Model.AgentRequests.Count);
        Assert.Equal("Items: 7, 8", FindResult(agentMessages, AgentApprovalTestHarness.ListCallId));
        Assert.Equal(ApprovalUnavailableAIFunction.GetMessage(DeleteToolName), FindResult(agentMessages, AgentApprovalTestHarness.DeleteCallId));
    }

    [Fact]
    public void AResumeOfAChat_IsClaimedOnce_UntilItIsDone()
    {
        using (var claim = ChatTurnResume.TryClaim("session-claim-1"))
        {
            Assert.NotNull(claim);

            // A second resume of the same chat, however its id is cased, waits for nothing and is turned away.
            Assert.Null(ChatTurnResume.TryClaim("SESSION-CLAIM-1"));

            using var other = ChatTurnResume.TryClaim("session-claim-2");
            Assert.NotNull(other);
        }

        using var again = ChatTurnResume.TryClaim("session-claim-1");
        Assert.NotNull(again);

        // Without a session there is nothing to claim; the caller reports the missing session.
        using var none = ChatTurnResume.TryClaim(null);
        Assert.NotNull(none);
    }

    [Fact]
    public void AFailedResume_PutsThePausedMessageBack_OnlyUntilAnApprovedCallStarted()
    {
        var activity = new ChatTurnToolActivity
        {
            ToolCalls =
            [
                new ChatTurnToolCall { CallId = "c1", Name = "delete_item", Arguments = "{}", Status = ChatTurnToolCallStatus.AwaitingApproval, ApprovalRequestId = "r1" },
                new ChatTurnToolCall { CallId = "c2", Name = "send_mail", Arguments = "{}", Status = ChatTurnToolCallStatus.AwaitingApproval, ApprovalRequestId = "r2" },
            ],
        };
        var waiting = ChatTurnResume.Copy(activity);
        using var scope = AIInvocationScope.Begin();

        ChatTurnResume.Apply(activity, CreateResumeRequest(new Dictionary<string, bool> { ["r1"] = true, ["r2"] = false }), [], new HashSet<string>(), scope.Context);

        // The copy still waits for the answers, and shares nothing with the message the answers were applied to.
        Assert.True(waiting.HasPendingCalls);
        Assert.All(waiting.ToolCalls, call => Assert.Equal(ChatTurnToolCallStatus.AwaitingApproval, call.Status));
        Assert.False(activity.HasPendingCalls);

        // Nothing ran yet, or only the rejected call got its refusal: the message can wait for the answers again.
        Assert.False(ChatTurnResume.HaveApprovedCallsStarted(activity, [], scope.Context));
        Assert.False(ChatTurnResume.HaveApprovedCallsStarted(activity, [new ChatTurnToolCall { CallId = "c2", Status = ChatTurnToolCallStatus.Completed, Result = "Rejected." }], scope.Context));

        // The approved call ran: answering again could run it twice, so the answers stand.
        Assert.True(ChatTurnResume.HaveApprovedCallsStarted(activity, [new ChatTurnToolCall { CallId = "c1", Status = ChatTurnToolCallStatus.Completed, Result = "Deleted." }], scope.Context));
    }

    [Fact]
    public void AnAgentThatTookItsRunBack_CountsAsStarted()
    {
        var activity = new ChatTurnToolActivity
        {
            ToolCalls =
            [
                new ChatTurnToolCall { CallId = AgentCallId, Name = AgentName, Arguments = "{}", Status = ChatTurnToolCallStatus.AwaitingApproval },
                new ChatTurnToolCall { CallId = $"{AgentCallId}:delete-1", Name = DeleteToolName, Arguments = "{}", Status = ChatTurnToolCallStatus.AwaitingApproval, ApprovalRequestId = $"{AgentCallId}:ficc_delete-1", AgentName = AgentName, AgentCallId = AgentCallId },
            ],
            AgentRuns =
            [
                new ChatTurnAgentRun
                {
                    CallId = AgentCallId,
                    AgentName = AgentName,
                    ToolCalls = [new ChatTurnToolCall { CallId = "delete-1", Name = DeleteToolName, Arguments = "{}", Status = ChatTurnToolCallStatus.AwaitingApproval, ApprovalRequestId = "ficc_delete-1" }],
                },
            ],
        };
        using var scope = AIInvocationScope.Begin();

        ChatTurnResume.Apply(activity, CreateResumeRequest(new Dictionary<string, bool> { [$"{AgentCallId}:ficc_delete-1"] = true }), [], new HashSet<string>(), scope.Context);

        Assert.False(ChatTurnResume.HaveApprovedCallsStarted(activity, [], scope.Context));

        // The agent took its run back, so it may have run the approved call.
        Assert.True(AgentToolApprovals.Get(scope.Context).TryTakeResumption(AgentCallId, out _));
        Assert.True(ChatTurnResume.HaveApprovedCallsStarted(activity, [], scope.Context));
    }

    private static async Task<ChatTurn> RunFirstTurnAsync(AgentApprovalTestHarness harness)
    {
        using var scope = AIInvocationScope.Begin();

        var history = new List<ChatMessage> { new(ChatRole.User, AgentApprovalTestHarness.Prompt) };
        var events = new List<BaseEvent>();
        var writer = CreateWriter(events, "run-1");

        return await RunTurnAsync(harness, scope.Context, history, [.. history], writer, events, "message-1");
    }

    /// <summary>
    /// Resumes a paused turn the way the chat hub does: reads the stored message back, applies the answer, and runs
    /// the assistant's side of the turn again from the history up to the paused message.
    /// </summary>
    private static async Task<ChatTurn> ResumeAsync(AgentApprovalTestHarness harness, ChatTurn paused, bool? approved, List<ChatMessage> history = null, bool supportsApproval = true)
    {
        using var scope = AIInvocationScope.Begin();

        var answers = new Dictionary<string, bool>();

        if (approved is not null)
        {
            foreach (var interrupt in paused.Writer.Interrupts)
            {
                answers[interrupt.Id] = approved.Value;
            }
        }

        var events = new List<BaseEvent>();
        var writer = CreateWriter(events, "run-2");
        var resumeHistory = (history ?? paused.History).ToList();

        // The calls of the paused message that have a result are replayed, as the history builder does.
        ChatConversationHistoryBuilder.AddToolCallMessages(resumeHistory, paused.Activity);
        ChatTurnResume.Apply(paused.Activity, CreateResumeRequest(answers), resumeHistory, writer.RejectedCallIds, scope.Context);

        return await RunTurnAsync(harness, scope.Context, resumeHistory, history ?? paused.History, writer, events, "message-2", supportsApproval);
    }

    private static async Task<ChatTurn> RunTurnAsync(
        AgentApprovalTestHarness harness,
        AIInvocationContext invocation,
        List<ChatMessage> history,
        List<ChatMessage> historyBeforeTheTurn,
        AGUIRunEventWriter writer,
        List<BaseEvent> events,
        string messageId,
        bool supportsApproval = true)
    {
        var updates = await harness.RunAssistantAsync(
            invocation,
            history,
            supportsApproval,
            update => writer.WriteUpdateAsync(update, messageId, TestContext.Current.CancellationToken));

        await writer.FinishAsync(TestContext.Current.CancellationToken);

        // The message is stored and read back, so what the resume works on went through the store's JSON.
        var stored = JsonSerializer.Serialize(new ChatTurnToolActivity
        {
            ToolCalls = writer.ToolCalls.ToList(),
            AgentRuns = writer.AgentRuns.ToList(),
        });

        return new ChatTurn
        {
            Updates = updates,
            Events = events,
            Writer = writer,
            History = historyBeforeTheTurn,
            Activity = JsonSerializer.Deserialize<ChatTurnToolActivity>(stored),
            Text = string.Concat(updates.Select(update => update.Text)),
        };
    }

    private static AGUIRunEventWriter CreateWriter(List<BaseEvent> events, string runId)
    {
        return new AGUIRunEventWriter("thread-1", runId, (evt, _) =>
        {
            events.Add(evt);

            return ValueTask.CompletedTask;
        });
    }

    private static AGUIRunRequest CreateResumeRequest(IReadOnlyDictionary<string, bool> answers, IReadOnlyDictionary<string, string> clientResults = null)
    {
        var input = new
        {
            threadId = "thread-1",
            runId = "run-2",
            messages = (clientResults ?? new Dictionary<string, string>())
                .Select(result => new { id = $"tool-{result.Key}", role = "tool", toolCallId = result.Key, content = result.Value })
                .ToArray(),
            tools = Array.Empty<object>(),
            context = Array.Empty<object>(),
            forwardedProps = new { profileId = "profile-1" },
            resume = answers
                .Select(answer => new { interruptId = answer.Key, status = "resolved", payload = new { approved = answer.Value } })
                .ToArray(),
        };

        return AGUIRunRequest.From(AGUIJson.DeserializeInput(JsonSerializer.SerializeToElement(input)));
    }

    private static string FindResult(List<ChatMessage> messages, string callId)
    {
        return messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .FirstOrDefault(result => result.CallId == callId)
            ?.Result
            ?.ToString();
    }

    /// <summary>
    /// One turn of the assistant, as the chat hub keeps it.
    /// </summary>
    private sealed class ChatTurn
    {
        public List<ChatResponseUpdate> Updates { get; init; }

        public List<BaseEvent> Events { get; init; }

        public AGUIRunEventWriter Writer { get; init; }

        /// <summary>
        /// Gets the conversation history before the turn's own assistant message.
        /// </summary>
        public List<ChatMessage> History { get; init; }

        /// <summary>
        /// Gets the tool calls stored with the turn's assistant message, read back from their stored form.
        /// </summary>
        public ChatTurnToolActivity Activity { get; init; }

        public string Text { get; init; }
    }
}
