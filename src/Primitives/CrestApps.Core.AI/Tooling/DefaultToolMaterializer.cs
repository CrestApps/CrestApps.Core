using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Default <see cref="IToolMaterializer"/>. Resolves each scoped <see cref="ToolRegistryEntry"/> to an
/// <see cref="AITool"/> via its factory, optionally enforcing per-user access for listable tools,
/// de-duplicating by function name, and ordering local/system tools ahead of MCP tools.
/// </summary>
public sealed class DefaultToolMaterializer : IToolMaterializer
{
    private readonly IAIToolAccessEvaluator _toolAccessEvaluator;
    private readonly IOptions<AIToolDefinitionOptions> _toolDefinitions;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DefaultToolMaterializer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultToolMaterializer"/> class.
    /// </summary>
    /// <param name="toolAccessEvaluator">The tool access evaluator (used when access enforcement is enabled).</param>
    /// <param name="toolDefinitions">The registered tool definitions, used to identify listable tools.</param>
    /// <param name="serviceProvider">The service provider passed to each tool factory.</param>
    /// <param name="logger">The logger.</param>
    public DefaultToolMaterializer(
        IAIToolAccessEvaluator toolAccessEvaluator,
        IOptions<AIToolDefinitionOptions> toolDefinitions,
        IServiceProvider serviceProvider,
        ILogger<DefaultToolMaterializer> logger)
    {
        _toolAccessEvaluator = toolAccessEvaluator;
        _toolDefinitions = toolDefinitions;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ToolMaterializationResult> MaterializeAsync(
        IReadOnlyList<ToolRegistryEntry> entries,
        ToolMaterializationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        options ??= ToolMaterializationOptions.Default;

        if (entries.Count == 0)
        {
            return new ToolMaterializationResult();
        }

        // A null principal means there is no caller at all (such as a background task), so the request is
        // treated as a trusted server-side invocation and the check is skipped.
        var enforceAccess = options.EnforceListableAccess && options.User is not null;

        var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tools = new List<AITool>(entries.Count);
        List<string> deniedToolNames = null;

        // Snapshot a stable partition before authorization or factory callbacks can mutate the source:
        // local/system tools keep their order, MCP tools are appended in original order after them.
        var orderedEntries = new ToolRegistryEntry[entries.Count];
        var nonMcpIndex = 0;
        var mcpIndex = orderedEntries.Length;

        foreach (var entry in entries)
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
            if (enforceAccess &&
                TryGetListableDefinition(entry, out var definition) &&
                !await _toolAccessEvaluator.IsAuthorizedAsync(options.User, entry.Name, definition))
            {
                (deniedToolNames ??= []).Add(entry.Name);

                AITelemetry.ToolDenials.Add(1, new KeyValuePair<string, object>(AITelemetry.ReasonTag, "access"));

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Tool '{ToolName}' from {Source} ({Id}) denied by access evaluator for the current caller.",
                        entry.Name, entry.Source, entry.Id);
                }

                continue;
            }

            // A tool that needs a person's approval is only offered when the caller can ask for one. Realtime,
            // background and other non-interactive requests have nobody to ask, so offering it there would run it
            // unasked. Where the approval is enforced (the run of an agent), the tool is offered instead as one that
            // does not run and tells the model why; see WrapForApproval.
            if (entry.RequiresApproval && !options.SupportsToolApproval && !options.EnforceToolApproval)
            {
                AITelemetry.ToolDenials.Add(1, new KeyValuePair<string, object>(AITelemetry.ReasonTag, "approval"));

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Tool '{ToolName}' from {Source} ({Id}) requires approval and the caller cannot ask for one. Skipping.",
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
                    tools.Add(WrapForApproval(tool, entry, options));
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

        return new ToolMaterializationResult
        {
            Tools = tools,
            DeniedToolNames = (IReadOnlyList<string>)deniedToolNames ?? [],
        };
    }

    /// <summary>
    /// Wraps a tool that requires an approval in <see cref="ApprovalRequiredAIFunction"/> when the caller can ask a
    /// person for one, so the function invocation pauses for the approval instead of running it. When the caller
    /// cannot ask and the approval is enforced (<see cref="ToolMaterializationOptions.EnforceToolApproval"/>), the
    /// tool is replaced with one that does not run and tells the model why.
    /// </summary>
    /// <param name="tool">The materialized tool.</param>
    /// <param name="entry">The registry entry the tool came from.</param>
    /// <param name="options">The materialization options.</param>
    /// <returns>The tool to offer.</returns>
#pragma warning disable MEAI001 // ApprovalRequiredAIFunction is a preview API of Microsoft.Extensions.AI.
    internal static AITool WrapForApproval(AITool tool, ToolRegistryEntry entry, ToolMaterializationOptions options)
    {
        if (!entry.RequiresApproval ||
            tool is not AIFunction function ||
            tool is ApprovalRequiredAIFunction ||
            tool is ApprovalUnavailableAIFunction)
        {
            return tool;
        }

        if (options.SupportsToolApproval)
        {
            return new ApprovalRequiredAIFunction(function);
        }

        if (options.EnforceToolApproval)
        {
            return new ApprovalUnavailableAIFunction(function);
        }

        return tool;
    }
#pragma warning restore MEAI001

    /// <summary>
    /// Gets the definition of an entry that represents a listable (user-selectable) tool. Only listable tools are
    /// subject to the per-user access check; system tools are auto-injected and hidden tools are
    /// dependency-only, so both bypass it. The access check accepts the tool's name or one of its aliases, so
    /// access granted for an earlier name of a renamed tool still applies.
    /// </summary>
    /// <param name="entry">The registry entry.</param>
    /// <param name="definition">The definition of the listable tool.</param>
    /// <returns><see langword="true"/> when the entry is a listable tool.</returns>
    private bool TryGetListableDefinition(ToolRegistryEntry entry, out AIToolDefinitionEntry definition)
    {
        return _toolDefinitions.Value.TryGetTool(entry.Name, out definition) && definition.IsSelectable();
    }
}
