using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Completion service handler that resolves scoped tool entries from the context
/// and configures <see cref="ChatOptions.Tools"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every entry reaching this handler has already been scoped to its resource's configuration
/// (profile- or interaction-selected tools, admin-attached MCP connections, and context-driven
/// system tools).
/// </para>
/// <para>
/// For an <strong>AI Session</strong>, the AI Profile is the authorization boundary: a session runs
/// the profile exactly as configured (it may be anonymous), so no per-user tool check is applied.
/// For a <strong>Chat Interaction</strong>, the caller-persisted tool selection is re-verified at
/// send time: each <em>listable</em> (user-selectable) tool is checked against
/// <see cref="IAIToolAccessEvaluator"/> so a tampered interaction cannot use a selectable tool the
/// caller lacks access to. System tools (auto-injected) and hidden/dependency tools are never
/// checked.
/// </para>
/// </remarks>
public sealed class FunctionInvocationAICompletionServiceHandler : IAICompletionServiceHandler
{
    /// <summary>
    /// Key used to store scoped <see cref="ToolRegistryEntry"/> instances in
    /// <see cref="AICompletionContext.AdditionalProperties"/> so the handler can
    /// resolve tools from their factories without a second registry lookup.
    /// </summary>
    public const string ScopedEntriesKey = "_scopedToolEntries";

    private readonly IAIToolAccessEvaluator _toolAccessEvaluator;
    private readonly IUserAccessor _userAccessor;
    private readonly IOptions<AIToolDefinitionOptions> _toolDefinitions;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FunctionInvocationAICompletionServiceHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionInvocationAICompletionServiceHandler"/> class.
    /// </summary>
    /// <param name="toolAccessEvaluator">The tool access evaluator (used for Chat Interaction requests).</param>
    /// <param name="userAccessor">The accessor that resolves the principal owning the current request.</param>
    /// <param name="toolDefinitions">The registered tool definitions, used to identify listable tools.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="logger">The logger.</param>
    public FunctionInvocationAICompletionServiceHandler(
        IAIToolAccessEvaluator toolAccessEvaluator,
        IUserAccessor userAccessor,
        IOptions<AIToolDefinitionOptions> toolDefinitions,
        IServiceProvider serviceProvider,
        ILogger<FunctionInvocationAICompletionServiceHandler> logger)
    {
        _toolAccessEvaluator = toolAccessEvaluator;
        _userAccessor = userAccessor;
        _toolDefinitions = toolDefinitions;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Configures the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task ConfigureAsync(CompletionServiceConfigureContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.IsFunctionInvocationSupported ||
            context.CompletionContext is null ||
                !context.CompletionContext.AdditionalProperties.TryGetValue(ScopedEntriesKey, out var entriesObj) ||
                    entriesObj is not IReadOnlyList<ToolRegistryEntry> scopedEntries ||
                        scopedEntries.Count == 0)
        {
            return;
        }

        context.ChatOptions.Tools ??= [];

        // The per-user access check applies to Chat Interaction requests only. AI Sessions run the
        // profile exactly as configured (the profile is the authorization boundary) and may be
        // anonymous, so no session-time tool gate is applied.
        var isChatInteraction = context.CompletionContext.AdditionalProperties.ContainsKey(AICompletionContextKeys.Interaction);

        // A null principal means there is no caller at all (such as a background task), so the
        // request is treated as a trusted server-side invocation and the check is skipped.
        var user = isChatInteraction ? _userAccessor.User : null;
        var enforceAccess = isChatInteraction && user is not null;

        var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> deniedToolNames = null;

        // Snapshot a stable partition before authorization or factory callbacks can mutate the source.
        var orderedEntries = new ToolRegistryEntry[scopedEntries.Count];
        var nonMcpIndex = 0;
        var mcpIndex = orderedEntries.Length;

        foreach (var entry in scopedEntries)
        {
            if (entry.Source == ToolRegistryEntrySource.McpServer)
            {
                orderedEntries[--mcpIndex] = entry;
            }
            else
            {
                orderedEntries[nonMcpIndex++] = entry;
            }
        }

        Array.Reverse(orderedEntries, mcpIndex, orderedEntries.Length - mcpIndex);

        foreach (var entry in orderedEntries)
        {
            // For Chat Interactions, verify the caller is allowed to use each listable (user-selectable)
            // tool that was persisted in the interaction's settings. System and hidden/dependency tools
            // are not user-selectable and are never checked.
            if (enforceAccess &&
                IsListable(entry) &&
                !await _toolAccessEvaluator.IsAuthorizedAsync(user, entry.Name))
            {
                (deniedToolNames ??= []).Add(entry.Name);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Tool '{ToolName}' from {Source} ({Id}) denied by access evaluator for the current Chat Interaction caller.",
                        entry.Name, entry.Source, entry.Id);
                }

                continue;
            }

            if (entry.CreateAsync is null)
            {
                _logger.LogWarning("Tool entry '{ToolName}' ({Id}) has no ToolFactory. Skipping.", entry.Name, entry.Id);
                continue;
            }

            // Skip duplicate function names.
            if (!addedNames.Add(entry.Name))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Skipping tool '{ToolName}' from {Source} ({Id}) - name already registered.",
                        entry.Name, entry.Source, entry.Id);
                }

                continue;
            }

            try
            {
                var tool = await entry.CreateAsync(_serviceProvider);

                if (tool is not null)
                {
                    context.ChatOptions.Tools.Add(tool);
                }
                else if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("ToolFactory returned null for '{ToolName}' ({Id}).", entry.Name, entry.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create tool '{ToolName}' ({Id}). Skipping.", entry.Name, entry.Id);
            }
        }

        if (deniedToolNames is not null)
        {
            // Surface the denial above Debug level. Otherwise the caller only sees a degraded
            // answer with no indication that the configured tools were removed.
            _logger.LogWarning(
                "The current Chat Interaction caller is not authorized to use the following listable AI tools, which were excluded from the request: {ToolNames}.",
                string.Join(", ", deniedToolNames));
        }
    }

    /// <summary>
    /// Determines whether an entry represents a listable (user-selectable) tool. Only listable tools
    /// are subject to the per-user access check; system tools are auto-injected and hidden tools are
    /// dependency-only, so both bypass it.
    /// </summary>
    private bool IsListable(ToolRegistryEntry entry)
    {
        return _toolDefinitions.Value.Tools.TryGetValue(entry.Name, out var definition) && definition.IsSelectable();
    }
}
