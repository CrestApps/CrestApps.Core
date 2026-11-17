using CrestApps.Core.AI.AGUI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Chat.Hubs;

/// <summary>
/// Applies the client's answers to an assistant message that paused for tool approvals or for the client's tool
/// results, and prepares the run that resumes it.
/// </summary>
internal static class ChatTurnResume
{
    /// <summary>
    /// Applies the client's answers to the calls of a paused assistant message and adds to the conversation
    /// history what the resumed run needs: the client's tool results, and the approval requests with the person's
    /// answers, which the tool-calling loop runs or rejects before it asks the model again.
    /// </summary>
    /// <remarks>
    /// A call an agent made is answered inside the agent, not by the assistant. For each agent that paused, the
    /// assistant's call to the agent is made again, and the agent's paused run is handed back with the answers
    /// (see <see cref="AgentToolApprovals"/>), so the agent continues where it stopped: the calls it made before
    /// are not made again, an approved call runs, and a rejected call is reported to the agent as rejected.
    /// </remarks>
    /// <param name="pausedActivity">The tool calls of the paused assistant message. Their statuses are updated, and the paused agent runs are taken from it.</param>
    /// <param name="request">The run request with the client's answers.</param>
    /// <param name="conversationHistory">The conversation history up to the paused message, which is added to.</param>
    /// <param name="rejectedCallIds">Receives the ids of the assistant's own calls the person rejected.</param>
    /// <param name="invocation">The invocation context of the run that resumes.</param>
#pragma warning disable MEAI001 // Tool approvals are a preview API of Microsoft.Extensions.AI.
    public static void Apply(
        ChatTurnToolActivity pausedActivity,
        AGUIRunRequest request,
        List<ChatMessage> conversationHistory,
        ISet<string> rejectedCallIds,
        AIInvocationContext invocation)
    {
        ArgumentNullException.ThrowIfNull(pausedActivity);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(conversationHistory);
        ArgumentNullException.ThrowIfNull(rejectedCallIds);

        var clientCalls = new List<AIContent>();
        var clientResults = new List<AIContent>();
        var approvalRequests = new List<AIContent>();
        var approvalResponses = new List<AIContent>();
        var pausedAgentRuns = new Dictionary<string, ChatTurnAgentRun>(StringComparer.Ordinal);

        foreach (var run in pausedActivity.AgentRuns ?? [])
        {
            if (run is not null && run.HasPendingCalls && !string.IsNullOrEmpty(run.CallId))
            {
                pausedAgentRuns[run.CallId] = run;
            }
        }

        foreach (var call in pausedActivity.ToolCalls)
        {
            var functionCall = new FunctionCallContent(call.CallId, call.Name, ChatConversationHistoryBuilder.ParseArguments(call.Arguments));

            if (call.Status == ChatTurnToolCallStatus.AwaitingClient)
            {
                var result = request.ClientToolResults.TryGetValue(call.CallId, out var clientResult)
                    ? clientResult
                    : "The client did not return a result for this tool call.";

                call.Result = Truncate(result, AGUIRunEventWriter.MaxToolResultLength);
                call.Status = ChatTurnToolCallStatus.Completed;
                clientCalls.Add(functionCall);
                clientResults.Add(new FunctionResultContent(call.CallId, call.Result));
            }
            else if (call.Status == ChatTurnToolCallStatus.AwaitingApproval && invocation is not null && pausedAgentRuns.ContainsKey(call.CallId))
            {
                // The assistant's call to an agent that paused. It is made again whatever the person answered, so
                // the agent can run what was approved and report what was rejected.
                var agentCallRequest = new ToolApprovalRequestContent($"agent_{call.CallId}", functionCall);

                call.Status = ChatTurnToolCallStatus.Approved;
                approvalRequests.Add(agentCallRequest);
                approvalResponses.Add(agentCallRequest.CreateResponse(approved: true, reason: null));
            }
            else if (call.Status == ChatTurnToolCallStatus.AwaitingApproval)
            {
                // A call the client did not answer is rejected: the protocol asks the client to answer every interrupt.
                request.TryGetApproval(call.ApprovalRequestId, out var approved);

                call.Status = approved
                    ? ChatTurnToolCallStatus.Approved
                    : ChatTurnToolCallStatus.Rejected;

                if (!string.IsNullOrEmpty(call.AgentCallId))
                {
                    // A call an agent made: the agent runs or rejects it when it resumes.
                    continue;
                }

                var approvalRequest = new ToolApprovalRequestContent(call.ApprovalRequestId ?? call.CallId, functionCall);

                if (!approved)
                {
                    rejectedCallIds.Add(call.CallId);
                }

                approvalRequests.Add(approvalRequest);
                approvalResponses.Add(approvalRequest.CreateResponse(approved, reason: null));
            }
        }

        if (invocation is not null)
        {
            foreach (var run in pausedAgentRuns.Values)
            {
                var decisions = new Dictionary<string, bool>(StringComparer.Ordinal);

                foreach (var waiting in run.ToolCalls)
                {
                    if (waiting.Status != ChatTurnToolCallStatus.AwaitingApproval)
                    {
                        continue;
                    }

                    var interruptId = AgentToolApprovals.ComposeId(run.CallId, waiting.ApprovalRequestId ?? waiting.CallId);

                    request.TryGetApproval(interruptId, out var approved);
                    decisions[interruptId] = approved;
                }

                AgentToolApprovals.GetOrAdd(invocation).AddResumption(run, decisions);
            }
        }

        // Each paused run is continued once: the resumed run keeps a run of its own if the agent pauses again.
        pausedActivity.AgentRuns = [];

        if (clientCalls.Count > 0)
        {
            conversationHistory.Add(new ChatMessage(ChatRole.Assistant, clientCalls));
            conversationHistory.Add(new ChatMessage(ChatRole.Tool, clientResults));
        }

        if (approvalRequests.Count > 0)
        {
            conversationHistory.Add(new ChatMessage(ChatRole.Assistant, approvalRequests));
            conversationHistory.Add(new ChatMessage(ChatRole.User, approvalResponses));
        }
    }
#pragma warning restore MEAI001

    /// <summary>
    /// Cuts a value to a length.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="maxLength">The most characters to keep.</param>
    /// <returns>The value, cut when it is longer.</returns>
    private static string Truncate(string value, int maxLength)
    {
        return string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
}
