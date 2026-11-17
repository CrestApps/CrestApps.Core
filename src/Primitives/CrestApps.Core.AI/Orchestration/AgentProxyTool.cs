using System.Diagnostics;
using System.Text.Json;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Tooling;
using Cysharp.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// An AI tool that proxies execution to an agent (AI profile with <see cref="AIProfileType.Agent"/> type).
/// When the AI model invokes this tool, it delegates the task to the agent's completion service
/// using the agent profile's configuration (system message, tools, MCP connections, etc.).
/// </summary>
internal sealed class AgentProxyTool : AIFunction
{
    /// <summary>
    /// The maximum sub-agent depth at which an agent is allowed to run its own tools.
    /// The top-level completion is depth <c>0</c>; a tool-capable agent runs at depth <c>1</c>.
    /// Because agents are never exposed as tools to other agents, depth cannot exceed this value
    /// in practice; the limit is a defensive backstop against runaway recursion.
    /// </summary>
    private const int MaxAgentToolInvocationDepth = 1;

    private readonly string _agentProfileName;
    private readonly string _description;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentProxyTool"/> class.
    /// </summary>
    /// <param name="agentProfileName">The agent profile name.</param>
    /// <param name="description">The description.</param>
    public AgentProxyTool(
        string agentProfileName,
        string description)
    {
        _agentProfileName = agentProfileName;
        _description = description;
    }

    public override string Name => _agentProfileName;

    public override string Description => _description;

    public override JsonElement JsonSchema { get; } = JsonElement.Parse("""
    {
      "type": "object",
      "properties": {
        "prompt": {
          "type": "string",
          "description": "The prompt or message to send to the agent for processing."
        }
      },
      "required": ["prompt"]
    }
    """);

    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>()
    {
        ["Strict"] = false,
    };

    /// <summary>
    /// Invokes core.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(arguments.Services);

        var logger = arguments.Services.GetRequiredService<ILogger<AgentProxyTool>>();

        // Both are read before the agent runs: the agent's own tool calls replace the ambient function invocation.
        var callId = AIToolInvocation.GetCallId(arguments);
        var functionInvocation = FunctionInvokingChatClient.CurrentContext;

        if (!arguments.TryGetFirstString("prompt", out var task) || string.IsNullOrWhiteSpace(task))
        {
            return "No prompt was provided to the agent.";
        }

        try
        {
            var profileManager = arguments.Services.GetRequiredService<IAIProfileManager>();
            var profiles = await profileManager.GetAsync(AIProfileType.Agent, cancellationToken);
            var agentProfile = profiles?.FirstOrDefault(p => string.Equals(p.Name, _agentProfileName, StringComparison.OrdinalIgnoreCase));

            if (agentProfile is null)
            {
                logger.LogWarning("Agent profile '{AgentName}' not found.", _agentProfileName);

                return $"Agent '{_agentProfileName}' is not available.";
            }

            if (ShouldRunWithTools(agentProfile))
            {
                return await RunWithToolsAsync(arguments.Services, agentProfile, task, callId, functionInvocation, cancellationToken);
            }

            return await RunWithoutToolsAsync(arguments.Services, agentProfile, task, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute agent '{AgentName}'.", _agentProfileName);

            return $"An error occurred while executing agent '{_agentProfileName}'.";
        }
    }

    private static bool ShouldRunWithTools(AIProfile agentProfile)
    {
        if (!agentProfile.TryGet<AgentMetadata>(out var agentMetadata) || !agentMetadata.AllowToolInvocation)
        {
            return false;
        }

        var depth = AIInvocationScope.Current?.AgentInvocationDepth;

        // A null scope means depth cannot be tracked, so we conservatively run without tools.
        return depth is >= 0 and < MaxAgentToolInvocationDepth;
    }

    /// <summary>
    /// Runs an agent that names no chat deployment of its own on the one the delegating conversation uses.
    /// </summary>
    /// <remarks>
    /// The system agents name none. Left to the site default, a conversation set to a capable model handed
    /// its PDF work to a smaller default one, which wandered through bookmark and link tools until it ran out
    /// of tool calls instead of building the document it was asked for.
    /// </remarks>
    /// <param name="context">The agent's completion context.</param>
    /// <param name="callerDeploymentName">The delegating conversation's chat deployment.</param>
    private static void InheritDeployment(AICompletionContext context, string callerDeploymentName)
    {
        if (string.IsNullOrEmpty(context.ChatDeploymentName) && !string.IsNullOrEmpty(callerDeploymentName))
        {
            context.ChatDeploymentName = callerDeploymentName;
        }
    }

    /// <summary>
    /// Gets the result of a call to an agent that paused because a tool call it made waits for a person's approval.
    /// The assistant's turn ends with the approval request, so the model reads this only when a tool-calling loop
    /// asks it again anyway.
    /// </summary>
    /// <param name="agentName">The name of the agent.</param>
    /// <param name="toolNames">The names of the tools whose calls wait for an approval.</param>
    /// <returns>The text of the result.</returns>
    internal static string GetPausedMessage(string agentName, IEnumerable<string> toolNames)
    {
        return $"The agent '{agentName}' paused. It needs the user's approval before it can run: {string.Join(", ", toolNames)}. The user is being asked now, and the agent continues from where it stopped when they answer. Do not call the agent again for this request, and do not say that the task is done.";
    }

    /// <summary>
    /// Runs the agent through the orchestrator so its configured tools are available, while
    /// suppressing nested agents and incrementing the recursion depth for the duration.
    /// </summary>
    /// <remarks>
    /// A tool of the agent that requires a person's approval never runs unasked. When the run that called the
    /// agent can ask a person (see <see cref="AgentToolApprovals"/>) and the id of this call is known, the agent
    /// pauses on such a tool call: its run so far is kept, the caller's turn ends with an approval request, and
    /// the call that resumes continues the agent from the kept run with the person's answer. Otherwise the tool
    /// does not run and the agent's model is told why.
    /// </remarks>
    /// <param name="services">The services of the request.</param>
    /// <param name="agentProfile">The agent's profile.</param>
    /// <param name="task">The task the caller gave the agent.</param>
    /// <param name="callId">The id of the caller's tool call to the agent, or <see langword="null"/> when it is not known.</param>
    /// <param name="functionInvocation">The caller's function invocation of this call, or <see langword="null"/> when the caller's tool-calling loop has none.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async ValueTask<object> RunWithToolsAsync(
        IServiceProvider services,
        AIProfile agentProfile,
        string task,
        string callId,
        FunctionInvocationContext functionInvocation,
        CancellationToken cancellationToken)
    {
        var orchestrationContextBuilder = services.GetRequiredService<IOrchestrationContextBuilder>();
        var orchestratorResolver = services.GetRequiredService<IOrchestratorResolver>();
        var invocationContext = AIInvocationScope.Current;
        var callerDeploymentName = invocationContext.CompletionContext?.ChatDeploymentName;
        var approvals = AgentToolApprovals.Get(invocationContext);

        // The agent can ask only when its caller can show the request to a person and can come back to this very
        // call with the answer.
        var canAsk = approvals?.CanAsk == true && !string.IsNullOrEmpty(callId);
        AgentRunResumption resumption = null;

        if (canAsk)
        {
            approvals.TryTakeResumption(callId, out resumption);
        }

        var run = resumption?.Run ?? new ChatTurnAgentRun
        {
            CallId = callId,
            AgentName = agentProfile.Name,
        };

        // Enter the sub-agent scope before building the context so that agent enrichment and
        // agent tools are suppressed for the agent's own orchestration.
        invocationContext.AgentInvocationDepth++;

        Activity.Current?.SetTag(AITelemetry.AgentDepthAttribute, invocationContext.AgentInvocationDepth);

        try
        {
            var orchestrationContext = await orchestrationContextBuilder.BuildAsync(
                agentProfile,
                ctx =>
                {
                    ctx.UserMessage = task;

                    if (ctx.CompletionContext is not null)
                    {
                        // Sub-agents never delegate to other agents.
                        ctx.CompletionContext.AgentNames = [];
                        InheritDeployment(ctx.CompletionContext, callerDeploymentName);
                        AgentToolApprovals.ConfigureAgentContext(ctx.CompletionContext, canAsk);
                    }
                },
                cancellationToken);

            if (resumption is not null)
            {
                // The agent goes on from what it did before it paused: the calls that ran are in the conversation
                // with their results, so they are not made again, and the waiting calls come with the answers.
                orchestrationContext.ConversationHistory = AgentRunHistory.BuildResumeHistory(task, resumption);

                // That conversation is one task of the agent, not past messages of a chat: a limit on past messages
                // must not cut the task or the agent's earlier calls off.
                if (orchestrationContext.CompletionContext is not null)
                {
                    orchestrationContext.CompletionContext.PastMessagesCount = null;
                }

                // The tools of the answered calls must still be offered, whatever tool scoping picks this time.
                foreach (var call in run.ToolCalls)
                {
                    if (call.Status is ChatTurnToolCallStatus.Approved or ChatTurnToolCallStatus.Rejected &&
                        !orchestrationContext.MustIncludeTools.Contains(call.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        orchestrationContext.MustIncludeTools.Add(call.Name);
                    }
                }
            }

            var orchestrator = orchestratorResolver.Resolve(agentProfile.OrchestratorName);

            using var builder = ZString.CreateStringBuilder();

            var updates = orchestrator.ExecuteStreamingAsync(orchestrationContext, cancellationToken)
                .WithOrchestrationTelemetry(orchestrator.Name, cancellationToken);

            await foreach (var update in updates)
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    builder.Append(update.Text);
                }

                if (canAsk)
                {
                    AgentRunHistory.Record(run, update);
                }
            }

            if (canAsk && run.HasPendingCalls)
            {
                approvals.Pause(
                    new FunctionCallContent(callId, agentProfile.Name, new Dictionary<string, object> { ["prompt"] = task }),
                    run);

                // The caller's turn ends after this round of tool calls: the person answers first.
                if (functionInvocation is not null)
                {
                    functionInvocation.Terminate = true;
                }

                return GetPausedMessage(
                    agentProfile.Name,
                    run.ToolCalls
                        .Where(call => call.Status == ChatTurnToolCallStatus.AwaitingApproval)
                        .Select(call => call.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase));
            }

            var result = builder.ToString();

            if (string.IsNullOrWhiteSpace(result))
            {
                return "The agent did not produce a response.";
            }

            return result;
        }
        finally
        {
            invocationContext.AgentInvocationDepth--;
        }
    }

    /// <summary>
    /// Runs the agent as an isolated single completion with tools disabled. This is the default,
    /// recursion-safe behavior used for agents that do not opt into tool invocation.
    /// </summary>
    private async ValueTask<object> RunWithoutToolsAsync(
        IServiceProvider services,
        AIProfile agentProfile,
        string task,
        CancellationToken cancellationToken)
    {
        var completionService = services.GetRequiredService<IAICompletionService>();
        var contextBuilder = services.GetRequiredService<IAICompletionContextBuilder>();
        var deploymentManager = services.GetRequiredService<IAIDeploymentManager>();

        var context = await contextBuilder.BuildAsync(agentProfile, cancellationToken: cancellationToken);

        InheritDeployment(context, AIInvocationScope.Current?.CompletionContext?.ChatDeploymentName);

        // Disable tools on the agent's context to prevent infinite recursion.
        context.DisableTools = true;
        context.AdditionalProperties[AICompletionContextKeys.UsagePurpose] = AIUsagePurposes.SubAgent;

        var deployment = await deploymentManager.ResolveSlotAsync(AIDeploymentSlotNames.Chat, deploymentName: context.ChatDeploymentName, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"Unable to resolve a chat deployment for agent profile '{_agentProfileName}'.");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, task),
        };

        var response = await completionService.CompleteAsync(
            deployment,
            messages,
            context,
            cancellationToken);

        var result = response?.Messages?.FirstOrDefault(m => m.Role == ChatRole.Assistant)?.Text;

        return result ?? "The agent did not produce a response.";
    }
}
