using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

                if (tool is null)
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("ToolFactory returned null for '{ToolName}' ({Id}).", entry.Name, entry.Id);
                    }

                    continue;
                }

                var offered = WrapForApproval(tool, entry, options, _logger);

                if (offered is null)
                {
                    // The tool requires a person's approval and cannot be wrapped for one, so it is never offered.
                    AITelemetry.ToolDenials.Add(1, new KeyValuePair<string, object>(AITelemetry.ReasonTag, "approval"));

                    _logger.LogWarning(
                        "Tool '{ToolName}' from {Source} ({Id}) requires approval but is not an AIFunction, so no approval can be asked for it. It is left out.",
                        entry.Name, entry.Source, entry.Id);
                }
                else if (offered is ApprovalUnavailableAIFunction)
                {
                    // The tool requires a person's approval and this request cannot ask for one, so it never runs.
                    AITelemetry.ToolDenials.Add(1, new KeyValuePair<string, object>(AITelemetry.ReasonTag, "approval"));

                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(
                            "Tool '{ToolName}' from {Source} ({Id}) requires approval and the caller cannot ask for one. It is offered as a tool that does not run.",
                            entry.Name, entry.Source, entry.Id);
                    }
                }

                if (offered is not null)
                {
                    tools.Add(offered);
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
    /// Prepares a tool that requires an approval for the request. When the caller can ask a person, the tool is
    /// wrapped in <see cref="ApprovalRequiredAIFunction"/>, so the function invocation pauses for the approval instead
    /// of running it. When the caller cannot ask, the tool never runs: it is replaced with a function of the same
    /// name and schema that does not run it and tells the model why, so the model can tell the user. When the
    /// request's approval policy lets the tool run without asking (see <see cref="ToolApprovalPolicy.RunsWithoutAsking"/>),
    /// it is wrapped in <see cref="AutoApprovedAIFunction"/>, which runs it and logs and counts each call. A tool that
    /// does not require an approval is returned as it is.
    /// </summary>
    /// <param name="tool">The materialized tool.</param>
    /// <param name="entry">The registry entry the tool came from.</param>
    /// <param name="options">The materialization options.</param>
    /// <param name="logger">The logger the calls that run without asking are logged to.</param>
    /// <returns>
    /// The tool to offer, or <see langword="null"/> when the tool must be approved and is not an
    /// <see cref="AIFunction"/>: such a tool can be neither wrapped for an approval nor replaced, nor audited when it
    /// runs without asking, so it is left out, whatever the caller and the policy.
    /// </returns>
#pragma warning disable MEAI001 // ApprovalRequiredAIFunction is a preview API of Microsoft.Extensions.AI.
    internal static AITool WrapForApproval(AITool tool, ToolRegistryEntry entry, ToolMaterializationOptions options, ILogger logger = null)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(entry);
        options ??= ToolMaterializationOptions.Default;

        if (!entry.RequiresApproval ||
            tool is ApprovalRequiredAIFunction ||
            tool is ApprovalUnavailableAIFunction ||
            tool is AutoApprovedAIFunction)
        {
            return tool;
        }

        // A tool that is not a function (a provider-hosted tool, for example) is not run by the function invocation,
        // so an approval cannot be asked for it: offering it would let it run unasked.
        if (tool is not AIFunction function)
        {
            return null;
        }

        // The approval policy lets the tool run without asking: its kind requires the approval, not its own
        // registration. The call is audited when it runs.
        if (options.ApprovalPolicy?.RunsWithoutAsking(entry) == true)
        {
            return new AutoApprovedAIFunction(function, entry.Kind, logger ?? NullLogger.Instance);
        }

        if (options.SupportsToolApproval)
        {
            return new ApprovalRequiredAIFunction(function);
        }

        return new ApprovalUnavailableAIFunction(function);
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
