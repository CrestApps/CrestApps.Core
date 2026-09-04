using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Security;
using Microsoft.Extensions.Logging;

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

    private readonly IToolMaterializer _toolMaterializer;
    private readonly IUserAccessor _userAccessor;
    private readonly ILogger<FunctionInvocationAICompletionServiceHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FunctionInvocationAICompletionServiceHandler"/> class.
    /// </summary>
    /// <param name="toolMaterializer">The shared scoped-entries-to-tools materializer.</param>
    /// <param name="userAccessor">The accessor that resolves the principal owning the current request.</param>
    /// <param name="logger">The logger.</param>
    public FunctionInvocationAICompletionServiceHandler(
        IToolMaterializer toolMaterializer,
        IUserAccessor userAccessor,
        ILogger<FunctionInvocationAICompletionServiceHandler> logger)
    {
        _toolMaterializer = toolMaterializer;
        _userAccessor = userAccessor;
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

        var result = await _toolMaterializer.MaterializeAsync(
            scopedEntries,
            new ToolMaterializationOptions
            {
                EnforceListableAccess = isChatInteraction,
                User = isChatInteraction ? _userAccessor.User : null,
            },
            cancellationToken);

        foreach (var tool in result.Tools)
        {
            context.ChatOptions.Tools.Add(tool);
        }

        if (result.DeniedToolNames.Count > 0)
        {
            // Surface the denial above Debug level. Otherwise the caller only sees a degraded
            // answer with no indication that the configured tools were removed.
            _logger.LogWarning(
                "The current Chat Interaction caller is not authorized to use the following listable AI tools, which were excluded from the request: {ToolNames}.",
                string.Join(", ", result.DeniedToolNames));
        }
    }
}
