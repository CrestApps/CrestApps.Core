using System.Text.Json;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Keeps what an agent did in a run that can pause for an approval, and turns it back into the conversation the
/// agent continues from. The tool calls that ran come back as calls with their results, so the model sees its
/// own work and the calls are not made again; the calls that waited come back as approval requests with the
/// person's answers, which the tool-calling loop runs or rejects.
/// </summary>
internal static class AgentRunHistory
{
    // Tool arguments are kept as compact JSON; the shared Microsoft.Extensions.AI options indent it.
    private static readonly JsonSerializerOptions _compactJsonOptions = new(AIJsonUtilities.DefaultOptions)
    {
        WriteIndented = false,
    };

    /// <summary>
    /// Records the tool calls, the results and the approval requests of one update of the agent's stream.
    /// </summary>
    /// <param name="run">The agent's run.</param>
    /// <param name="update">The update.</param>
#pragma warning disable MEAI001 // Tool approvals are a preview API of Microsoft.Extensions.AI.
    public static void Record(ChatTurnAgentRun run, ChatResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(update);

        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call when !string.IsNullOrEmpty(call.CallId):
                    GetOrAddCall(run, call);
                    break;

                case FunctionResultContent result when !string.IsNullOrEmpty(result.CallId):
                    var answered = run.ToolCalls.FirstOrDefault(call => call.CallId == result.CallId);

                    if (answered is not null)
                    {
                        answered.Result = Truncate(ResultToText(result));

                        // A call a person rejected keeps that status; its result is the rejection the model reads.
                        if (answered.Status != ChatTurnToolCallStatus.Rejected)
                        {
                            answered.Status = ChatTurnToolCallStatus.Completed;
                        }
                    }

                    break;

                case ToolApprovalRequestContent approval when approval.ToolCall is FunctionCallContent waiting && !string.IsNullOrEmpty(waiting.CallId):
                    var record = GetOrAddCall(run, waiting);

                    // A call whose tool needs no approval waits only because its round has a call that does. Nobody is
                    // asked for it, and the resume runs it.
                    if (ToolApprovalRequests.NeedsNoApproval(approval))
                    {
                        record.NeedsNoApproval = true;

                        break;
                    }

                    record.Status = ChatTurnToolCallStatus.AwaitingApproval;
                    record.ApprovalRequestId = approval.RequestId;
                    break;
            }
        }
    }

    /// <summary>
    /// Builds the conversation an agent continues from after a person answered its approval requests, and marks
    /// each waiting call of the run as approved or rejected.
    /// </summary>
    /// <param name="task">The task the assistant gave the agent.</param>
    /// <param name="resumption">The paused run and the answers.</param>
    /// <returns>The conversation: the task, the calls that ran with their results, the approval requests and the answers.</returns>
    public static List<ChatMessage> BuildResumeHistory(string task, AgentRunResumption resumption)
    {
        ArgumentNullException.ThrowIfNull(resumption);

        var run = resumption.Run;
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, task),
        };

        var answered = run.ToolCalls
            .Where(call => call.Status is ChatTurnToolCallStatus.Completed or ChatTurnToolCallStatus.Rejected &&
                call.Result is not null &&
                !string.IsNullOrEmpty(call.CallId))
            .ToList();

        if (answered.Count > 0)
        {
            history.Add(new ChatMessage(ChatRole.Assistant, answered
                .Select(call => (AIContent)new FunctionCallContent(call.CallId, call.Name, ParseArguments(call.Arguments)))
                .ToList()));
            history.Add(new ChatMessage(ChatRole.Tool, answered
                .Select(call => (AIContent)new FunctionResultContent(call.CallId, call.Result))
                .ToList()));
        }

        var requests = new List<AIContent>();
        var responses = new List<AIContent>();

        foreach (var call in run.ToolCalls)
        {
            var needsNoApproval = call.NeedsNoApproval && call.Status == ChatTurnToolCallStatus.Requested;

            if (call.Status != ChatTurnToolCallStatus.AwaitingApproval && !needsNoApproval)
            {
                continue;
            }

            // A call whose tool needs no approval runs whatever the person answered to the others, as it would have alone.
            var requestId = call.ApprovalRequestId ?? call.CallId;
            var approved = needsNoApproval ||
                (resumption.Decisions.TryGetValue(AgentToolApprovals.ComposeId(run.CallId, requestId), out var decision) && decision);
            var request = new ToolApprovalRequestContent(requestId, new FunctionCallContent(call.CallId, call.Name, ParseArguments(call.Arguments)));

            call.Status = approved
                ? ChatTurnToolCallStatus.Approved
                : ChatTurnToolCallStatus.Rejected;

            requests.Add(request);
            responses.Add(request.CreateResponse(approved, reason: null));
        }

        if (requests.Count > 0)
        {
            history.Add(new ChatMessage(ChatRole.Assistant, requests));
            history.Add(new ChatMessage(ChatRole.User, responses));
        }

        return history;
    }
#pragma warning restore MEAI001

    /// <summary>
    /// Reads the stored arguments of a tool call.
    /// </summary>
    /// <param name="arguments">The arguments, as a JSON object.</param>
    /// <returns>The arguments.</returns>
    public static Dictionary<string, object> ParseArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(arguments, AIJsonUtilities.DefaultOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Gets the record of a tool call of the run, and adds it when the run has none for the call's id.
    /// </summary>
    /// <param name="run">The agent's run.</param>
    /// <param name="call">The tool call.</param>
    /// <returns>The record of the call.</returns>
    private static ChatTurnToolCall GetOrAddCall(ChatTurnAgentRun run, FunctionCallContent call)
    {
        var record = run.ToolCalls.FirstOrDefault(candidate => candidate.CallId == call.CallId);

        if (record is not null)
        {
            return record;
        }

        record = new ChatTurnToolCall
        {
            CallId = call.CallId,
            Name = call.Name,
            Arguments = JsonSerializer.Serialize(call.Arguments ?? new Dictionary<string, object>(), _compactJsonOptions),
            Status = ChatTurnToolCallStatus.Requested,
        };

        run.ToolCalls.Add(record);

        return record;
    }

    /// <summary>
    /// Gets the text of a tool result, as the model read it.
    /// </summary>
    /// <param name="result">The tool result.</param>
    /// <returns>The text.</returns>
    private static string ResultToText(FunctionResultContent result)
    {
        return result.Result switch
        {
            null => result.Exception?.Message ?? string.Empty,
            string text => text,
            JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText(),
            _ => JsonSerializer.Serialize(result.Result, _compactJsonOptions),
        };
    }

    /// <summary>
    /// Cuts the text of a tool result to the most characters a run keeps.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The text, cut and marked when it is longer.</returns>
    private static string Truncate(string text)
    {
        return text.Length > ChatTurnAgentRun.MaxToolResultLength
            ? string.Concat(text.AsSpan(0, ChatTurnAgentRun.MaxToolResultLength), "…")
            : text;
    }
}
