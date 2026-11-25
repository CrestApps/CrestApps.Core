using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Marks the tool approval requests (<see cref="ToolApprovalRequestContent"/>) of calls that need no approval of their
/// own, and reads the mark.
/// </summary>
/// <remarks>
/// <c>FunctionInvokingChatClient</c> of Microsoft.Extensions.AI, and the Azure OpenAI client's own tool-calling loop
/// like it, turn every call of a round of tool calls into an approval request once one call of the round requires an
/// approval. A completion client marks the request of each call whose tool requires none, from the tools it offered
/// the model. Whatever shows the round to a person asks only for the other calls, and the resume approves the marked
/// calls itself, so they run once whatever the person answers, as they would have alone. Only the server sets the
/// mark: nothing a client sends becomes an approval request.
/// </remarks>
#pragma warning disable MEAI001 // Tool approvals are a preview API of Microsoft.Extensions.AI.
public static class ToolApprovalRequests
{
    /// <summary>
    /// The key of the additional property (<see langword="true"/>) that marks the approval request of a call whose tool
    /// needs no approval.
    /// </summary>
    public const string NeedsNoApprovalKey = "crestapps.needs_no_approval";

    /// <summary>
    /// Determines whether a tool waits for a person's approval before it runs: it is, or wraps, an
    /// <see cref="ApprovalRequiredAIFunction"/>.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <returns><see langword="true"/> when the tool requires an approval.</returns>
    public static bool RequiresApproval(AITool tool)
    {
        return tool?.GetService<ApprovalRequiredAIFunction>() is not null;
    }

    /// <summary>
    /// Marks each approval request among the contents whose call is to a function the tool-calling loop runs itself
    /// and that requires no approval. A request for a tool that requires one, for a tool the client runs (a
    /// declaration), or for a tool that was not offered keeps no mark, so a person is asked.
    /// </summary>
    /// <param name="contents">The contents of an update or a message of the loop.</param>
    /// <param name="tools">The tools the loop offered the model.</param>
    public static void MarkCallsThatNeedNoApproval(IEnumerable<AIContent> contents, IEnumerable<AITool> tools)
    {
        if (contents is null || tools is null)
        {
            return;
        }

        foreach (var request in contents.OfType<ToolApprovalRequestContent>())
        {
            if (request.ToolCall is not FunctionCallContent call)
            {
                continue;
            }

            var tool = tools.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Name, StringComparison.Ordinal));

            if (tool is AIFunction && !RequiresApproval(tool))
            {
                request.AdditionalProperties ??= new AdditionalPropertiesDictionary();
                request.AdditionalProperties[NeedsNoApprovalKey] = true;
            }
        }
    }

    /// <summary>
    /// Marks each approval request of a response whose call needs no approval (see
    /// <see cref="MarkCallsThatNeedNoApproval(IEnumerable{AIContent}, IEnumerable{AITool})"/>).
    /// </summary>
    /// <param name="response">The response of the loop.</param>
    /// <param name="tools">The tools the loop offered the model.</param>
    public static void MarkCallsThatNeedNoApproval(ChatResponse response, IEnumerable<AITool> tools)
    {
        foreach (var message in response?.Messages ?? [])
        {
            MarkCallsThatNeedNoApproval(message.Contents, tools);
        }
    }

    /// <summary>
    /// Determines whether an approval request is for a call that needs no approval of its own, and waits only because
    /// it shares a round of tool calls with one that does.
    /// </summary>
    /// <param name="request">The approval request.</param>
    /// <returns><see langword="true"/> when the request is marked.</returns>
    public static bool NeedsNoApproval(ToolApprovalRequestContent request)
    {
        return request?.AdditionalProperties is not null &&
            request.AdditionalProperties.TryGetValue(NeedsNoApprovalKey, out var value) &&
            value is true;
    }
}
#pragma warning restore MEAI001
