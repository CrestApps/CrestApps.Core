using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Carries, for one invocation, the approvals that tool calls inside agents wait for. An agent runs inside a
/// tool call of the assistant, so it cannot end the assistant's run itself. Three parties meet here:
/// the orchestrator of the assistant's turn says that it can show an approval request to a person
/// (<see cref="AllowAsking"/>); an agent whose tool call needs an approval leaves its paused run
/// (<see cref="Pause"/>), which the orchestrator turns into approval requests at the end of its stream
/// (<see cref="TakeApprovalRequests"/>); and the caller that resumes the turn hands back each paused run with
/// the person's answers (<see cref="AddResumption"/>), which the agent picks up when it is called again
/// (<see cref="TryTakeResumption"/>).
/// </summary>
internal sealed class AgentToolApprovals
{
    private const string ItemKey = "CrestApps.Core.AI.AgentToolApprovals";

    private readonly Lock _lock = new();
    private readonly Dictionary<string, AgentRunResumption> _resumptions = new(StringComparer.Ordinal);
    private readonly List<(FunctionCallContent AgentCall, ChatTurnAgentRun Run)> _pauses = [];
    private readonly HashSet<string> _pausedCallIds = new(StringComparer.Ordinal);
    private bool _tookResumption;

    /// <summary>
    /// Gets a value indicating whether an agent took its paused run back in this invocation (see
    /// <see cref="TryTakeResumption"/>), so a call the person approved may have run.
    /// </summary>
    public bool HasTakenResumptions
    {
        get
        {
            lock (_lock)
            {
                return _tookResumption;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the run that calls the agents can show an approval request to a person and
    /// come back to the agent with the answer.
    /// </summary>
    public bool CanAsk { get; private set; }

    /// <summary>
    /// Gets a value indicating whether an agent paused in the assistant's current turn, which ends the turn after
    /// the current round of tool calls. It is cleared when the turn's approval requests are taken
    /// (<see cref="TakeApprovalRequests"/>), so a later completion in the same invocation is not cut short.
    /// </summary>
    public bool HasPausedRuns
    {
        get
        {
            lock (_lock)
            {
                return _pausedCallIds.Count > 0;
            }
        }
    }

    /// <summary>
    /// Gets the approvals of the given invocation, when it has any.
    /// </summary>
    /// <param name="invocation">The invocation context.</param>
    /// <returns>The approvals, or <see langword="null"/>.</returns>
    public static AgentToolApprovals Get(AIInvocationContext invocation)
    {
        return invocation is not null && invocation.Items.TryGetValue(ItemKey, out var value)
            ? value as AgentToolApprovals
            : null;
    }

    /// <summary>
    /// Gets the approvals of the given invocation, and adds them when it has none.
    /// </summary>
    /// <param name="invocation">The invocation context.</param>
    /// <returns>The approvals.</returns>
    public static AgentToolApprovals GetOrAdd(AIInvocationContext invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var approvals = Get(invocation);

        if (approvals is null)
        {
            approvals = new AgentToolApprovals();
            invocation.Items[ItemKey] = approvals;
        }

        return approvals;
    }

    /// <summary>
    /// Determines whether the assistant's turn waits for an approval inside an agent. It is <see langword="false"/>
    /// while an agent runs, so the pause of one agent never cuts short another agent that the same round of tool
    /// calls started. A tool-calling loop does not call this: it calls <see cref="AIToolLoop.ShouldEndTurn"/>,
    /// which covers this and every other reason to end the turn after a round of tool calls.
    /// </summary>
    /// <param name="invocation">The invocation context.</param>
    /// <returns><see langword="true"/> when the assistant's turn waits for an approval inside an agent.</returns>
    public static bool IsTurnPaused(AIInvocationContext invocation)
    {
        return invocation is not null &&
            invocation.AgentInvocationDepth == 0 &&
            Get(invocation)?.HasPausedRuns == true;
    }

    /// <summary>
    /// Makes the id under which a tool call or an approval request of an agent is shown in the assistant's run.
    /// The id of the assistant's call to the agent comes first, so the ids of two agents never collide with each
    /// other or with the assistant's own calls.
    /// </summary>
    /// <param name="agentCallId">The id of the assistant's call to the agent.</param>
    /// <param name="id">The id of the agent's tool call or approval request.</param>
    /// <returns>The id for the assistant's run.</returns>
    public static string ComposeId(string agentCallId, string id)
    {
        return $"{agentCallId}:{id}";
    }

    /// <summary>
    /// Marks the completion context of an agent's run: the agent can ask for an approval only when its caller can.
    /// Otherwise a tool of the agent that requires an approval does not run, as in any request that cannot ask.
    /// </summary>
    /// <param name="agentContext">The completion context of the agent's run.</param>
    /// <param name="canAsk">Whether the agent's caller can ask a person for an approval.</param>
    public static void ConfigureAgentContext(AICompletionContext agentContext, bool canAsk)
    {
        ArgumentNullException.ThrowIfNull(agentContext);

        if (canAsk)
        {
            agentContext.AdditionalProperties[AICompletionContextKeys.ToolApprovalSupported] = true;
        }
        else
        {
            agentContext.AdditionalProperties.Remove(AICompletionContextKeys.ToolApprovalSupported);
        }
    }

    /// <summary>
    /// Says that the run that calls the agents can show an approval request to a person and resume.
    /// </summary>
    public void AllowAsking()
    {
        CanAsk = true;
    }

    /// <summary>
    /// Hands back the paused run of an agent with the person's answers, for the agent to continue from when the
    /// assistant's call to it is made again.
    /// </summary>
    /// <param name="run">The paused run.</param>
    /// <param name="decisions">
    /// The answers, keyed by the id each approval request had in the assistant's run (see <see cref="ComposeId"/>).
    /// A request without an answer counts as rejected.
    /// </param>
    public void AddResumption(ChatTurnAgentRun run, IReadOnlyDictionary<string, bool> decisions)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrEmpty(run.CallId);

        lock (_lock)
        {
            _resumptions[run.CallId] = new AgentRunResumption(run, decisions ?? new Dictionary<string, bool>());
        }
    }

    /// <summary>
    /// Takes the paused run the assistant's call to an agent continues from, when there is one. A run is taken
    /// once, so the call continues it only once.
    /// </summary>
    /// <param name="agentCallId">The id of the assistant's call to the agent.</param>
    /// <param name="resumption">The paused run and the answers.</param>
    /// <returns><see langword="true"/> when the call continues a paused run.</returns>
    public bool TryTakeResumption(string agentCallId, out AgentRunResumption resumption)
    {
        resumption = null;

        if (string.IsNullOrEmpty(agentCallId))
        {
            return false;
        }

        lock (_lock)
        {
            if (!_resumptions.Remove(agentCallId, out resumption))
            {
                return false;
            }

            _tookResumption = true;

            return true;
        }
    }

    /// <summary>
    /// Records that an agent paused because tool calls it made wait for an approval.
    /// </summary>
    /// <param name="agentCall">The assistant's call to the agent.</param>
    /// <param name="run">The agent's run so far.</param>
    public void Pause(FunctionCallContent agentCall, ChatTurnAgentRun run)
    {
        ArgumentNullException.ThrowIfNull(agentCall);
        ArgumentNullException.ThrowIfNull(run);

        lock (_lock)
        {
            _pauses.Add((agentCall, run));
            _pausedCallIds.Add(agentCall.CallId);
        }
    }

    /// <summary>
    /// Removes from an update of the assistant's stream the result of a call to an agent that paused. The call has
    /// no result yet: the agent answers it when it resumes.
    /// </summary>
    /// <param name="update">The update.</param>
    /// <returns>The update to pass on, or <see langword="null"/> when nothing of it is left.</returns>
    public ChatResponseUpdate RemovePausedResults(ChatResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (_lock)
        {
            if (_pausedCallIds.Count == 0 ||
                !update.Contents.Any(content => content is FunctionResultContent result && _pausedCallIds.Contains(result.CallId)))
            {
                return update;
            }

            var contents = update.Contents
                .Where(content => content is not FunctionResultContent result || !_pausedCallIds.Contains(result.CallId))
                .ToList();

            if (contents.Count == 0)
            {
                return null;
            }

            var remaining = update.Clone();

            remaining.Contents = contents;

            return remaining;
        }
    }

    /// <summary>
    /// Takes the approval requests for the agents that paused: one update for each agent, with one request for
    /// each of its tool calls that waits. Each request carries the assistant's call to the agent and the agent's
    /// paused run (see <see cref="AgentToolApprovalProperties"/>).
    /// </summary>
    /// <returns>The updates to add to the end of the assistant's stream.</returns>
#pragma warning disable MEAI001 // Tool approvals are a preview API of Microsoft.Extensions.AI.
    public IReadOnlyList<ChatResponseUpdate> TakeApprovalRequests()
    {
        List<(FunctionCallContent AgentCall, ChatTurnAgentRun Run)> pauses;

        lock (_lock)
        {
            // The turn ends with these requests, so the pause is over for this invocation: a later completion in the
            // same scope is neither ended early nor stripped of its results.
            _pausedCallIds.Clear();

            if (_pauses.Count == 0)
            {
                return [];
            }

            pauses = [.. _pauses];
            _pauses.Clear();
        }

        var updates = new List<ChatResponseUpdate>(pauses.Count);

        foreach (var (agentCall, run) in pauses)
        {
            var requests = new List<AIContent>();

            foreach (var call in run.ToolCalls)
            {
                if (call.Status != ChatTurnToolCallStatus.AwaitingApproval)
                {
                    continue;
                }

                var toolCall = new FunctionCallContent(
                    ComposeId(agentCall.CallId, call.CallId),
                    call.Name,
                    AgentRunHistory.ParseArguments(call.Arguments));

                requests.Add(new ToolApprovalRequestContent(ComposeId(agentCall.CallId, call.ApprovalRequestId ?? call.CallId), toolCall)
                {
                    AdditionalProperties = new AdditionalPropertiesDictionary
                    {
                        [AgentToolApprovalProperties.AgentCall] = agentCall,
                        [AgentToolApprovalProperties.AgentRun] = run,
                    },
                });
            }

            if (requests.Count > 0)
            {
                updates.Add(new ChatResponseUpdate(ChatRole.Assistant, requests));
            }
        }

        return updates;
    }
#pragma warning restore MEAI001
}
