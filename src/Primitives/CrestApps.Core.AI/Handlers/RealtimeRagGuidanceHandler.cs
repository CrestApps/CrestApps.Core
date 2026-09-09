using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Services;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Injects the knowledge-base guidance for realtime (speech-to-speech) sessions that have a data source
/// configured.
/// </summary>
/// <remarks>
/// <para>
/// In a live audio session there is no up-front user message, so the shared
/// <see cref="PreemptiveRagOrchestrationHandler"/> (which keys off the user message) runs no preemptive vector
/// search and injects no guidance. Which guidance belongs here depends on how the session will retrieve:
/// </para>
/// <list type="bullet">
/// <item>
/// A session that grounds every turn (<see cref="IRealtimeTurnGrounding"/>) is handed its knowledge before it
/// answers, so it gets the same response guidelines the text path uses after a preemptive search.
/// </item>
/// <item>
/// A session that does not gets the directive the text path uses when preemptive RAG is disabled — search the
/// knowledge base with the tool — honoring the profile's <c>IsInScope</c> strictness.
/// </item>
/// </list>
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

        // A grounded session retrieves the knowledge for every turn and hands it to the model before it answers,
        // so telling the model it must go and search is both untrue and expensive — it would add a tool round-trip
        // to every spoken turn on top of the search that already ran. This mirrors the text path, which likewise
        // only injects search-tool instructions when preemptive retrieval did not run.
        if (IsGrounded(orchestrationContext))
        {
            var guidelines = await _templateService.RenderAsync(AITemplateIds.RagResponseGuidelines, cancellationToken: cancellationToken);

            if (!string.IsNullOrEmpty(guidelines))
            {
                orchestrationContext.SystemMessageBuilder.AppendLine();
                orchestrationContext.SystemMessageBuilder.AppendLine(guidelines);
            }

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

    private static bool IsGrounded(OrchestrationContext context)
    {
        return context.Properties.TryGetValue(RealtimeOrchestrationContextKeys.GroundingEnabled, out var value) && value is true;
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
