using CrestApps.Core.AI.Models;
using CrestApps.Core.Security;
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
                IsListable(entry) &&
                !await _toolAccessEvaluator.IsAuthorizedAsync(options.User, entry.Name))
            {
                (deniedToolNames ??= []).Add(entry.Name);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Tool '{ToolName}' from {Source} ({Id}) denied by access evaluator for the current caller.",
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
                    tools.Add(tool);
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
    /// Determines whether an entry represents a listable (user-selectable) tool. Only listable tools are
    /// subject to the per-user access check; system tools are auto-injected and hidden tools are
    /// dependency-only, so both bypass it.
    /// </summary>
    private bool IsListable(ToolRegistryEntry entry)
    {
        return _toolDefinitions.Value.Tools.TryGetValue(entry.Name, out var definition) && definition.IsSelectable();
    }
}
