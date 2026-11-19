using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Stands in for a tool that requires a person's approval where no person can be asked: any request that does not
/// support tool approvals, such as the text-only chat stream, a chat interaction, a realtime conversation, background
/// work, or an agent whose caller cannot ask. It has the tool's name, description and schema, so the model sees the
/// same tool, but a call does not run the tool: it returns a message that says why, which the model can pass on.
/// </summary>
internal sealed class ApprovalUnavailableAIFunction : DelegatingAIFunction
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApprovalUnavailableAIFunction"/> class.
    /// </summary>
    /// <param name="innerFunction">The tool that requires an approval.</param>
    public ApprovalUnavailableAIFunction(AIFunction innerFunction)
        : base(innerFunction)
    {
    }

    /// <summary>
    /// Gets the message the model reads in place of the result of a tool that was not run.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <returns>The message.</returns>
    public static string GetMessage(string toolName)
    {
        return $"The tool '{toolName}' was not run. It needs a person's approval before each call, and this conversation cannot ask a person for one. Do not call it again. Tell the user that this action was not done, and that it needs a chat in which they can approve it.";
    }

    /// <summary>
    /// Answers the call without running the tool.
    /// </summary>
    /// <param name="arguments">The arguments the model chose.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The message that says why the tool was not run.</returns>
    protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // The model reads a result, so the call is counted and traced as a failure by this report.
        AIToolInvocation.ReportFailure();

        return ValueTask.FromResult<object>(GetMessage(Name));
    }
}
