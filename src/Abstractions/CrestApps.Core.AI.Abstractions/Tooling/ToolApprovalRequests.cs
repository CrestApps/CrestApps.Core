using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Marks the tool approval requests (<see cref="ToolApprovalRequestContent"/>) whose call needs no approval of its
/// own. Once one call of a round of tool calls requires an approval, Microsoft.Extensions.AI's function-invoking chat
/// client asks for an approval of every call of the round, and a completion client that runs its own tool-calling loop
/// does the same. The mark lets whatever shows the requests to a person leave those calls out, and the run that
/// resumes approve them itself.
/// </summary>
/// <remarks>
/// A request is marked where it is made, from the tools of that very request: an agent has tools of its own, so the
/// name of a tool does not say whether a call to it needs an approval. Only a call to a function the server runs, and
/// that does not require an approval, is marked. Any other request, including one for a tool the client runs or a
/// tool the request does not offer, keeps asking.
/// </remarks>
public static class ToolApprovalRequests
{
    /// <summary>
    /// The key of the additional property that marks a request whose call needs no approval of its own.
    /// </summary>
    public const string NeedsNoApprovalProperty = "crestapps.needs_no_approval";

    /// <summary>
    /// Marks each tool approval request among the contents whose call is to a function of <paramref name="tools"/>
    /// that does not require an approval.
    /// </summary>
    /// <param name="contents">The contents of a response message or of a streaming update.</param>
    /// <param name="tools">The tools of the request that made the calls.</param>
#pragma warning disable MEAI001 // Tool approvals are a preview API of Microsoft.Extensions.AI.
    public static void MarkCallsThatNeedNoApproval(IEnumerable<AIContent> contents, IEnumerable<AITool> tools)
    {
        if (contents is null || tools is null)
        {
            return;
        }

        foreach (var content in contents)
        {
            if (content is not ToolApprovalRequestContent { ToolCall: FunctionCallContent call } request)
            {
                continue;
            }

            var function = tools
                .OfType<AIFunction>()
                .FirstOrDefault(tool => string.Equals(tool.Name, call.Name, StringComparison.Ordinal));

            if (function is null || function.GetService<ApprovalRequiredAIFunction>() is not null)
            {
                continue;
            }

            request.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            request.AdditionalProperties[NeedsNoApprovalProperty] = true;
        }
    }

    /// <summary>
    /// Determines whether a tool approval request was marked as one whose call needs no approval of its own.
    /// </summary>
    /// <param name="request">The tool approval request.</param>
    /// <returns><see langword="true"/> when the call needs no approval of its own.</returns>
    public static bool NeedsNoApproval(ToolApprovalRequestContent request)
    {
        return request?.AdditionalProperties is not null &&
            request.AdditionalProperties.TryGetValue(NeedsNoApprovalProperty, out var value) &&
            value is true;
    }
#pragma warning restore MEAI001
}
