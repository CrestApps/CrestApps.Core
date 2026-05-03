using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tools;
using CrestApps.Core.Templates.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Handlers;

internal sealed class AIMemoryPreemptiveRagHandler : IPreemptiveRagHandler
{
    private readonly IAIMemorySearchService _memorySearchService;
    private readonly ITemplateService _templateService;
    private readonly IOptionsMonitor<GeneralAIOptions> _generalAIOptions;
    private readonly IOptionsMonitor<ChatInteractionMemoryOptions> _chatInteractionMemoryOptions;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AIMemoryPreemptiveRagHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIMemoryPreemptiveRagHandler"/> class.
    /// </summary>
    /// <param name="memorySearchService">The memory search service.</param>
    /// <param name="templateService">The template service.</param>
    /// <param name="generalAIOptions">The general ai options monitor.</param>
    /// <param name="chatInteractionMemoryOptions">The chat interaction memory options.</param>
    /// <param name="httpContextAccessor">The http context accessor.</param>
    /// <param name="logger">The logger.</param>
    public AIMemoryPreemptiveRagHandler(
        IAIMemorySearchService memorySearchService,
        ITemplateService templateService,
        IOptionsMonitor<GeneralAIOptions> generalAIOptions,
        IOptionsMonitor<ChatInteractionMemoryOptions> chatInteractionMemoryOptions,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AIMemoryPreemptiveRagHandler> logger)
    {
        _memorySearchService = memorySearchService;
        _templateService = templateService;
        _generalAIOptions = generalAIOptions;
        _chatInteractionMemoryOptions = chatInteractionMemoryOptions;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Determines whether handle.
    /// </summary>
    /// <param name="context">The context.</param>
    public async ValueTask<bool> CanHandleAsync(OrchestrationContextBuiltContext context)
    {
        var userId = AIMemoryOrchestrationContextHelper.GetAuthenticatedUserId(_httpContextAccessor);

        if (string.IsNullOrEmpty(userId))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("AI memory preemptive RAG skipped for {ResourceType}: user is not authenticated.", context.Resource.GetType().Name);
            }

            return false;
        }

        if (!_generalAIOptions.CurrentValue.EnablePreemptiveMemoryRetrieval)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("AI memory preemptive RAG skipped for {ResourceType}: preemptive memory retrieval is disabled.", context.Resource.GetType().Name);
            }

            return false;
        }

        var isEnabled = AIMemoryOrchestrationContextHelper.IsEnabled(context.Resource, _chatInteractionMemoryOptions.CurrentValue);

        if (!isEnabled && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("AI memory preemptive RAG skipped for {ResourceType}: memory is disabled.", context.Resource.GetType().Name);
        }

        return isEnabled;
    }

    /// <summary>
    /// Handles the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public async Task HandleAsync(PreemptiveRagContext context)
    {
        var userId = AIMemoryOrchestrationContextHelper.GetAuthenticatedUserId(_httpContextAccessor);

        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        var results = (await _memorySearchService.SearchAsync(userId, context.Queries, requestedTopN: null))
            .Where(result => !string.IsNullOrWhiteSpace(result.Content))
            .ToList();

        if (results.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("AI memory preemptive RAG found no matching memories for {QueryCount} query candidate(s).", context.Queries.Count);
            }

            return;
        }

        var arguments = new Dictionary<string, object>
        {
            ["results"] = results.Select(result => new
            {
                result.Name,
                result.Description,
                result.Content,
                UpdatedUtc = result.UpdatedUtc?.ToString("O"),
            }).ToList(),
        };

        if (!context.OrchestrationContext.DisableTools)
        {
            arguments["searchToolName"] = SearchUserMemoriesTool.TheName;
        }

        var header = await _templateService.RenderAsync(MemoryConstants.TemplateIds.MemoryContextHeader, arguments);

        if (!string.IsNullOrEmpty(header))
        {
            context.OrchestrationContext.SystemMessageBuilder.AppendLine();
            context.OrchestrationContext.SystemMessageBuilder.AppendLine();
            context.OrchestrationContext.SystemMessageBuilder.Append(header);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("AI memory preemptive RAG injected {ResultCount} memory entries into the system message.", results.Count);
        }
    }
}
