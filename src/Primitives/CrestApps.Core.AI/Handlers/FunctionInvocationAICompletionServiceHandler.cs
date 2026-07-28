using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Security;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Completion service handler that resolves scoped tool entries from the context,
/// evaluates tool-level authorization, and configures <see cref="ChatOptions.Tools"/>.
/// </summary>
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
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FunctionInvocationAICompletionServiceHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionInvocationAICompletionServiceHandler"/> class.
    /// </summary>
    /// <param name="toolAccessEvaluator">The tool access evaluator.</param>
    /// <param name="userAccessor">The accessor that resolves the principal owning the current request.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="logger">The logger.</param>
    public FunctionInvocationAICompletionServiceHandler(
        IAIToolAccessEvaluator toolAccessEvaluator,
        IUserAccessor userAccessor,
        IServiceProvider serviceProvider,
        ILogger<FunctionInvocationAICompletionServiceHandler> logger)
    {
        _toolAccessEvaluator = toolAccessEvaluator;
        _userAccessor = userAccessor;
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

        var user = _userAccessor.User;
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
            // Evaluate authorization for all tool sources exposed through the local orchestrator.
            // Although this handler only runs for the local orchestrator (external orchestrators
            // like Claude/Copilot manage their own tool selection), MCP tools surfaced here should
            // still be subject to the same per-user access policy as Local tools to prevent
            // unauthorized invocation via prompt injection.
            // A null principal means there is no caller at all, such as a background task, so the
            // request is treated as a trusted server-side invocation. An unauthenticated caller is
            // represented by a non-null principal and is still evaluated.
            if (user is not null && !await _toolAccessEvaluator.IsAuthorizedAsync(user, entry.Name))
            {
                (deniedToolNames ??= []).Add(entry.Name);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Tool '{ToolName}' from {Source} ({Id}) denied by access evaluator.",
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
                "The current user is not authorized to use the following AI tools, which were excluded from the request: {ToolNames}.",
                string.Join(", ", deniedToolNames));
        }
    }
}
