using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Provides agent profiles as tool entries in the tool registry.
/// Each AI profile with <see cref="AIProfileType.Agent"/> type is exposed
/// as a <see cref="ToolRegistryEntry"/> so the AI model can invoke agents as tools.
/// </summary>
/// <remarks>
/// <para>
/// This provider is registered as scoped to ensure it always reads the latest
/// agent profiles without requiring a tenant restart.
/// </para>
/// <para>
/// For a <strong>Chat Interaction</strong> request, each selected on-demand agent is checked against
/// <see cref="IAIAgentAccessEvaluator"/> for the current caller, and an agent the caller may not use is
/// left out. Always-available and system agents are never checked, a <see langword="null"/> caller (a
/// trusted server-side call) skips the check, and AI Sessions are not checked because the profile is the
/// authorization boundary there.
/// </para>
/// </remarks>
internal sealed class AgentToolRegistryProvider : IToolRegistryProvider
{
    private readonly IAIProfileManager _profileManager;
    private readonly IAIAgentAccessEvaluator _agentAccessEvaluator;
    private readonly IUserAccessor _userAccessor;
    private readonly ILogger<AgentToolRegistryProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentToolRegistryProvider"/> class.
    /// </summary>
    /// <param name="profileManager">The profile manager.</param>
    /// <param name="agentAccessEvaluator">The evaluator that decides which agents the caller may use.</param>
    /// <param name="userAccessor">The accessor that resolves the principal owning the current request.</param>
    /// <param name="logger">The logger.</param>
    public AgentToolRegistryProvider(
        IAIProfileManager profileManager,
        IAIAgentAccessEvaluator agentAccessEvaluator,
        IUserAccessor userAccessor,
        ILogger<AgentToolRegistryProvider> logger)
    {
        _profileManager = profileManager;
        _agentAccessEvaluator = agentAccessEvaluator;
        _userAccessor = userAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Gets tools.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<IReadOnlyList<ToolRegistryEntry>> GetToolsAsync(
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        // Prevent agents from being exposed as tools to other agents. When already running
        // inside a sub-agent (depth > 0), agent tools are suppressed so an agent can never
        // invoke another agent (or itself), which bounds recursion.
        if (AIInvocationScope.Current?.AgentInvocationDepth > 0)
        {
            return [];
        }

        var agents = await _profileManager.GetAsync(AIProfileType.Agent, cancellationToken);
        var entries = new List<ToolRegistryEntry>();

        if (agents is null)
        {
            return entries;
        }

        var requestedAgents = context?.AgentNames;

        // The per-user agent check applies to Chat Interaction requests only. AI Sessions run the
        // profile exactly as configured (the profile is the authorization boundary). A null principal
        // means there is no caller at all (such as a background task), so the check is skipped.
        var user = context is not null && context.AdditionalProperties.ContainsKey(AICompletionContextKeys.Interaction)
            ? _userAccessor.User
            : null;

        foreach (var agent in agents)
        {
            if (string.IsNullOrEmpty(agent.Name) || string.IsNullOrEmpty(agent.Description))
            {
                continue;
            }

            var isAlwaysAvailable = agent.IsAlwaysAvailableAgent();

            // Always-available agents are automatically included in every request.
            // On-demand agents are only included if explicitly selected via AgentNames.
            if (!isAlwaysAvailable && requestedAgents is not { Length: > 0 })
            {
                continue;
            }

            if (!isAlwaysAvailable && !requestedAgents.Contains(agent.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (user is not null &&
                !isAlwaysAvailable &&
                !agent.IsSystemAgent() &&
                !await _agentAccessEvaluator.IsAuthorizedAsync(user, agent))
            {
                AITelemetry.ToolDenials.Add(1, new KeyValuePair<string, object>(AITelemetry.ReasonTag, "access"));

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Agent '{AgentName}' ({Id}) denied by the agent access evaluator for the current caller.",
                        agent.Name, agent.ItemId);
                }

                continue;
            }

            var agentName = agent.Name;
            entries.Add(new ToolRegistryEntry
            {
                Id = $"agent:{agentName}",
                Name = agentName,
                Description = agent.Description,
                Source = ToolRegistryEntrySource.Agent,
                SourceId = agent.ItemId,
                CreateAsync = (sp) => ValueTask.FromResult<AITool>(new AgentProxyTool(agentName, agent.Description)),
            });
        }

        return entries;
    }
}
