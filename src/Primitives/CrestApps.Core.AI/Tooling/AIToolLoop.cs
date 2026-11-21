using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Helps a completion client that runs its own tool-calling loop (instead of <c>FunctionInvokingChatClient</c>)
/// behave like the framework's loop.
/// </summary>
public static class AIToolLoop
{
    /// <summary>
    /// Determines whether the turn ends after the current round of tool calls, without asking the model again: a
    /// tool asked for it (<see cref="AICompletionContextKeys.EndTurnAfterToolCalls"/>, for example one that opened
    /// another page), or an agent the assistant called paused because a tool call it made waits for a person's
    /// approval. Check it after each round of tool calls, and before asking the model when a resumed run's approved
    /// calls have just run.
    /// </summary>
    /// <remarks>
    /// Only the assistant's own turn ends: while an agent runs (<see cref="AIInvocationContext.AgentInvocationDepth"/>
    /// above zero) it is <see langword="false"/>, so a call of the same round that ends the turn never cuts short an
    /// agent another call of that round started. The assistant's loop ends after the round.
    /// </remarks>
    /// <param name="invocation">The invocation context, usually <see cref="AIInvocationScope.Current"/>.</param>
    /// <returns><see langword="true"/> when the turn ends after the current round of tool calls.</returns>
    public static bool ShouldEndTurn(AIInvocationContext invocation)
    {
        if (invocation is null || invocation.AgentInvocationDepth > 0)
        {
            return false;
        }

        return invocation.Items.ContainsKey(AICompletionContextKeys.EndTurnAfterToolCalls) ||
            AgentToolApprovals.IsTurnPaused(invocation);
    }
}
