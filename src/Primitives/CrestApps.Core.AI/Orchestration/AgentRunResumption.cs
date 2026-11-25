using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// The paused run of an agent, with a person's answers to the approvals it waits for.
/// </summary>
internal sealed class AgentRunResumption
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AgentRunResumption"/> class.
    /// </summary>
    /// <param name="run">The paused run.</param>
    /// <param name="decisions">The answers, keyed by the id each approval request had in the assistant's run.</param>
    public AgentRunResumption(
        ChatTurnAgentRun run,
        IReadOnlyDictionary<string, bool> decisions)
    {
        Run = run;
        Decisions = decisions;
    }

    /// <summary>
    /// Gets the paused run.
    /// </summary>
    public ChatTurnAgentRun Run { get; }

    /// <summary>
    /// Gets the answers, keyed by the id each approval request had in the assistant's run. A request without an
    /// answer counts as rejected.
    /// </summary>
    public IReadOnlyDictionary<string, bool> Decisions { get; }
}
