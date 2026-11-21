using System.Collections.Concurrent;
using System.Text.Json;
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
    // The sessions a resume runs for in this process. Ignoring case, like the session lookup.
    private static readonly ConcurrentDictionary<string, byte> _claims = new(StringComparer.OrdinalIgnoreCase);

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
            var functionCall = new FunctionCallContent(call.CallId, call.Name, AgentRunHistory.ParseArguments(call.Arguments));

            if (call.Status == ChatTurnToolCallStatus.AwaitingClient)
            {
                var result = request.ClientToolResults.TryGetValue(call.CallId, out var clientResult)
                    ? clientResult
                    : "The client did not return a result for this tool call.";

                call.Result = ChatText.Truncate(result, AGUIRunEventWriter.MaxToolResultLength);
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

        // Each paused run is continued once: the resumed run keeps a run of its own if the agent pauses again. Runs
        // are cleared only once they were handed back; the caller restores them when the resume fails before any
        // approved call ran (see HaveApprovedCallsStarted).
        if (invocation is not null)
        {
            pausedActivity.AgentRuns = [];
        }

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
    /// Claims the resume of a chat session for this process, so two resumes of one paused message never overlap: two
    /// that did (two tabs, a client that retries after a dropped connection) would both find the calls waiting and
    /// both run the approved ones.
    /// </summary>
    /// <remarks>
    /// The claim is per process. A host that runs several instances behind a load balancer also needs sticky routing
    /// for a session, or a store that checks concurrency, for the claim to hold across them.
    /// </remarks>
    /// <param name="sessionId">The id of the chat session.</param>
    /// <returns>The claim, to dispose when the resume is done; <see langword="null"/> when another resume of the session runs.</returns>
    public static IDisposable TryClaim(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            // Nothing can be resumed without a session; the caller reports that.
            return Claim.None;
        }

        var key = sessionId.Trim();

        return _claims.TryAdd(key, 0)
            ? new Claim(key)
            : null;
    }

    /// <summary>
    /// Copies the tool calls of a paused message, so they can be put back when the resume fails.
    /// </summary>
    /// <param name="activity">The tool calls of the paused message.</param>
    /// <returns>A copy that shares nothing with <paramref name="activity"/>.</returns>
    public static ChatTurnToolActivity Copy(ChatTurnToolActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return JsonSerializer.Deserialize<ChatTurnToolActivity>(JsonSerializer.SerializeToUtf8Bytes(activity));
    }

    /// <summary>
    /// Determines whether anything the person approved started to run in a resumed run: an approved call of the
    /// assistant has a result, or an agent took its paused run back. Until then a failed resume can put the paused
    /// message back as it was and let the person answer again; after that, answering again could run an approved
    /// call twice, so the answers stand.
    /// </summary>
    /// <param name="appliedActivity">The tool calls of the paused message, with the answers applied.</param>
    /// <param name="resumedCalls">The tool calls the resumed run wrote so far.</param>
    /// <param name="invocation">The invocation context of the resumed run.</param>
    /// <returns><see langword="true"/> when an approved call may have run.</returns>
    public static bool HaveApprovedCallsStarted(
        ChatTurnToolActivity appliedActivity,
        IReadOnlyList<ChatTurnToolCall> resumedCalls,
        AIInvocationContext invocation)
    {
        ArgumentNullException.ThrowIfNull(appliedActivity);

        if (AgentToolApprovals.Get(invocation)?.HasTakenResumptions == true)
        {
            return true;
        }

        var approvedCallIds = appliedActivity.ToolCalls
            .Where(call => call.Status == ChatTurnToolCallStatus.Approved &&
                string.IsNullOrEmpty(call.AgentCallId) &&
                !string.IsNullOrEmpty(call.CallId))
            .Select(call => call.CallId)
            .ToHashSet(StringComparer.Ordinal);

        return approvedCallIds.Count > 0 &&
            (resumedCalls ?? []).Any(call => call.CallId is not null &&
                approvedCallIds.Contains(call.CallId) &&
                (call.Status == ChatTurnToolCallStatus.Completed || call.Result is not null));
    }

    /// <summary>
    /// A claim on the resume of a session, released when disposed.
    /// </summary>
    private sealed class Claim : IDisposable
    {
        public static readonly Claim None = new(key: null);

        private string _key;

        public Claim(string key)
        {
            _key = key;
        }

        public void Dispose()
        {
            var key = Interlocked.Exchange(ref _key, null);

            if (key is not null)
            {
                _claims.TryRemove(key, out _);
            }
        }
    }
}
