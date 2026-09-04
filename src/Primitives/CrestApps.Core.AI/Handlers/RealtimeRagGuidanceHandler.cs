using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Services;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Injects "call the search tool" guidance for realtime (speech-to-speech) sessions that have a data
/// source configured.
/// </summary>
/// <remarks>
/// <para>
/// In a live audio session there is no up-front user message, so the shared
/// <see cref="PreemptiveRagOrchestrationHandler"/> (which keys off the user message) runs no preemptive
/// vector search and injects no guidance. Knowledge retrieval in realtime must instead be surfaced as a
/// callable search tool. This handler adds the same grounding directive the text path uses when
/// preemptive RAG is disabled, honoring the profile's <c>IsInScope</c> strictness, without modifying any
/// existing handler.
/// </para>
/// <para>
/// It only acts when <see cref="OrchestrationContext.ExecutionMode"/> is
/// <see cref="OrchestrationExecutionMode.Realtime"/>, so the text chat path is completely unaffected.
/// </para>
/// </remarks>
internal sealed class RealtimeRagGuidanceHandler : IOrchestrationContextBuilderHandler
{
    private readonly ITemplateService _templateService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RealtimeRagGuidanceHandler"/> class.
    /// </summary>
    /// <param name="templateService">The template service.</param>
    public RealtimeRagGuidanceHandler(ITemplateService templateService)
    {
        _templateService = templateService;
    }

    /// <inheritdoc />
    public Task BuildingAsync(OrchestrationContextBuildingContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task BuiltAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken = default)
    {
        var orchestrationContext = context.OrchestrationContext;

        if (orchestrationContext.ExecutionMode != OrchestrationExecutionMode.Realtime)
        {
            return;
        }

        // No data source, or tools disabled (no search tool to call) => nothing to guide.
        if (orchestrationContext.DisableTools ||
            orchestrationContext.CompletionContext is null ||
            string.IsNullOrWhiteSpace(orchestrationContext.CompletionContext.DataSourceId))
        {
            return;
        }

        var ragMetadata = GetRagMetadata(context.Resource);

        // IsInScope ON: the model MUST call the search tool and MUST NOT use general knowledge.
        // IsInScope OFF: the model MUST try the search tool first, then may supplement with general knowledge.
        var templateId = ragMetadata?.IsInScope == true
            ? AITemplateIds.RagToolSearchStrict
            : AITemplateIds.RagToolSearchRelaxed;

        var prompt = await _templateService.RenderAsync(templateId, CreateSearchToolArguments(), cancellationToken);

        if (!string.IsNullOrEmpty(prompt))
        {
            orchestrationContext.SystemMessageBuilder.AppendLine();
            orchestrationContext.SystemMessageBuilder.AppendLine(prompt);
        }
    }

    private static Dictionary<string, object> CreateSearchToolArguments()
    {
        return new()
        {
            ["searchToolNames"] = new[]
            {
                SystemToolNames.SearchDataSources,
                SystemToolNames.SearchDocuments,
            },
        };
    }

    private static AIDataSourceRagMetadata GetRagMetadata(object resource)
    {
        if (resource is AIProfile profile && profile.TryGet<AIDataSourceRagMetadata>(out var ragMetadata))
        {
            return ragMetadata;
        }

        if (resource is ChatInteraction interaction && interaction.TryGet<AIDataSourceRagMetadata>(out var interactionRagMetadata))
        {
            return interactionRagMetadata;
        }

        return null;
    }
}
