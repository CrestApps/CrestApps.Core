using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Handlers;

internal sealed class DataSourceOrchestrationHandler : IOrchestrationContextBuilderHandler
{
    private readonly AIToolDefinitionOptions _toolDefinitions;
    private readonly ITemplateService _templateService;
    private readonly ILogger<DataSourceOrchestrationHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSourceOrchestrationHandler"/> class.
    /// </summary>
    /// <param name="toolDefinitions">The tool definitions.</param>
    /// <param name="templateService">The template service.</param>
    /// <param name="logger">The logger.</param>
    public DataSourceOrchestrationHandler(
        IOptions<AIToolDefinitionOptions> toolDefinitions,
        ITemplateService templateService,
        ILogger<DataSourceOrchestrationHandler> logger)
    {
        _toolDefinitions = toolDefinitions.Value;
        _templateService = templateService;
        _logger = logger;
    }

    /// <summary>
    /// Buildings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public Task BuildingAsync(OrchestrationContextBuildingContext context)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builts the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public async Task BuiltAsync(OrchestrationContextBuiltContext context)
    {
        if (context.OrchestrationContext.CompletionContext == null || string.IsNullOrWhiteSpace(context.OrchestrationContext.CompletionContext.DataSourceId))
        {
            return;
        }

        var dataSourceTools = _toolDefinitions.Tools.Where(tool => tool.Value.HasPurpose(AIToolPurposes.DataSourceSearch)).Select(tool => tool.Value).ToList();
        if (!context.OrchestrationContext.DisableTools)
        {
            context.OrchestrationContext.MustIncludeTools.AddRange(dataSourceTools.Select(tool => tool.Name));
        }

        var arguments = new Dictionary<string, object>
        {
            ["tools"] = dataSourceTools,
        };
        if (!context.OrchestrationContext.DisableTools)
        {
            arguments["searchToolName"] = SystemToolNames.SearchDataSources;
        }

        var header = await _templateService.RenderAsync(AITemplateIds.DataSourceAvailability, arguments);
        if (!string.IsNullOrEmpty(header))
        {
            context.OrchestrationContext.SystemMessageBuilder.AppendLine();
            context.OrchestrationContext.SystemMessageBuilder.Append(header);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Enabled data-source orchestration for {ResourceType} and exposed tools: {Tools}.", context.Resource.GetType().Name, string.Join(", ", dataSourceTools.Select(tool => tool.Name)));
        }
    }
}
