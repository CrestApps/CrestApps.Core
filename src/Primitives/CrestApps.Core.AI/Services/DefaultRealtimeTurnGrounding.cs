using System.Diagnostics;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default <see cref="IRealtimeTurnGrounding"/>. Reuses the very same <see cref="IPreemptiveRagHandler"/>
/// pipeline the text path runs — data source, documents and memory — but drives it once per spoken utterance
/// instead of once per prepared request.
/// </summary>
/// <remarks>
/// Each turn runs against a scratch <see cref="OrchestrationContext"/> that shares the session's completion
/// context (so the data source, documents and tool state are identical) but carries its own system-message
/// builder and property bag. That keeps the retrieved block for this turn separate from the session's standing
/// instructions, which must not grow with every question the user asks.
/// </remarks>
internal sealed class DefaultRealtimeTurnGrounding : IRealtimeTurnGrounding
{
    private static readonly string[] _referencePropertyKeys = ["DataSourceReferences", "DocumentReferences"];

    private readonly IEnumerable<IPreemptiveRagHandler> _handlers;
    private readonly ITemplateService _templateService;
    private readonly DefaultOrchestratorSettings _settings;
    private readonly RealtimeTransportOptions _transportOptions;
    private readonly ILogger<DefaultRealtimeTurnGrounding> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultRealtimeTurnGrounding"/> class.
    /// </summary>
    /// <param name="handlers">The preemptive RAG handlers.</param>
    /// <param name="templateService">Renders the scope guidance that accompanies what was (or was not) retrieved.</param>
    /// <param name="settings">The orchestrator settings carrying the preemptive RAG switch.</param>
    /// <param name="transportOptions">The realtime transport options carrying the voice-only grounding switch.</param>
    /// <param name="logger">The logger.</param>
    public DefaultRealtimeTurnGrounding(
        IEnumerable<IPreemptiveRagHandler> handlers,
        ITemplateService templateService,
        IOptionsMonitor<DefaultOrchestratorSettings> settings,
        IOptions<RealtimeTransportOptions> transportOptions,
        ILogger<DefaultRealtimeTurnGrounding> logger)
    {
        _handlers = handlers;
        _templateService = templateService;
        _settings = settings.CurrentValue;
        _transportOptions = transportOptions?.Value ?? new RealtimeTransportOptions();
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsGroundingAvailable(OrchestrationContext context, object resource)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resource);

        // Two switches, deliberately. The site-wide one governs both paths: a host that has turned preemptive
        // retrieval off wants the model to decide when to search, in voice exactly as in text. The transport one
        // turns grounding off for voice alone, for a host that wants text grounded but voice as quick to start
        // speaking as possible.
        if (!_settings.EnablePreemptiveRag)
        {
            _logger.LogInformation("Realtime knowledge grounding is off: preemptive RAG is disabled site-wide (Settings > Enable preemptive RAG). Knowledge retrieval relies on the model calling the search tool.");

            return false;
        }

        if (!_transportOptions.EnableKnowledgeGrounding)
        {
            _logger.LogInformation("Realtime knowledge grounding is off: CrestApps:AI:RealtimeTransport:EnableKnowledgeGrounding is false. Knowledge retrieval relies on the model calling the search tool.");

            return false;
        }

        if (context.CompletionContext is null)
        {
            return false;
        }

        // Nothing to ground against. Documents count as well as data sources, because a session-attached
        // document is knowledge the user expects the assistant to have read.
        var hasDataSource = !string.IsNullOrWhiteSpace(context.CompletionContext.DataSourceId);
        var hasDocuments = context.CompletionContext.AdditionalProperties is not null
            && context.CompletionContext.AdditionalProperties.TryGetValue(AICompletionContextKeys.HasDocuments, out var value)
            && value is true;

        var hasHandlers = _handlers.Any();
        var available = (hasDataSource || hasDocuments) && hasHandlers;

        // Once per session; this is the first line to read when a knowledge question comes back ungrounded.
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Realtime knowledge grounding availability: {Available} (dataSource={HasDataSource}, documents={HasDocuments}, handlers={HasHandlers}).",
                available, hasDataSource, hasDocuments, hasHandlers);
        }

        return available;
    }

    /// <inheritdoc />
    public async Task<string> RetrieveAsync(
        OrchestrationContext context,
        object resource,
        string utterance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resource);

        if (string.IsNullOrWhiteSpace(utterance))
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var turnContext = CreateTurnContext(context, utterance);
        var builtContext = new OrchestrationContextBuiltContext(resource, turnContext);
        var usableHandlers = new List<IPreemptiveRagHandler>();

        foreach (var handler in _handlers)
        {
            if (await handler.CanHandleAsync(builtContext))
            {
                usableHandlers.Add(handler);
            }
        }

        if (usableHandlers.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Realtime retrieval skipped: no preemptive RAG handler accepted this turn.");
            }

            return null;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Realtime retrieval starting for utterance ({Length} chars) across {HandlerCount} handler(s): {Handlers}.",
                utterance.Length, usableHandlers.Count, string.Join(", ", usableHandlers.Select(h => h.GetType().Name)));
        }

        // The text path rewrites the user's message into focused queries with a utility LLM call first. That is
        // skipped here: it would add a round-trip to every spoken turn — the one place latency is audible — and it
        // earns that cost by resolving follow-ups against conversation history, which a per-turn realtime context
        // does not carry. The handlers search the utterance itself, which is already a spoken question.
        var ragContext = new PreemptiveRagContext(turnContext, resource, []);

        foreach (var handler in usableHandlers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await handler.HandleAsync(ragContext);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One failing source must not silence the whole turn: the model still answers, just with less
                // context, which is better than a conversation that stops replying.
                _logger.LogError(ex, "Realtime preemptive RAG handler '{HandlerType}' failed.", handler.GetType().Name);
            }
        }

        var referenceCount = PromoteReferences(turnContext);

        // The same scope guidance the text path attaches after its preemptive search. For a profile restricted
        // to retrieved data it is what stands between "nothing was found" and an answer from the model's own
        // knowledge — which is exactly the failure a restricted profile exists to prevent.
        await AppendScopeGuidanceAsync(turnContext, resource, referenceCount > 0, cancellationToken);

        var retrieved = turnContext.SystemMessageBuilder.ToString();

        stopwatch.Stop();

        if (string.IsNullOrWhiteSpace(retrieved))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Realtime retrieval found no relevant content for the current utterance ({ElapsedMs} ms).",
                    stopwatch.ElapsedMilliseconds);
            }

            return null;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Realtime retrieval returned {Length} chars and {ReferenceCount} citation(s) in {ElapsedMs} ms.",
                retrieved.Length, referenceCount, stopwatch.ElapsedMilliseconds);
        }

        return retrieved;
    }

    /// <summary>
    /// Mirrors <c>PreemptiveRagOrchestrationHandler</c>: an in-scope profile is told to stay within what was
    /// retrieved, or — when nothing was — to say so and search rather than improvise. A profile that is not
    /// in scope already carries the response guidelines in its session instructions, so nothing is added.
    /// </summary>
    private async Task AppendScopeGuidanceAsync(OrchestrationContext turnContext, object resource, bool hasReferences, CancellationToken cancellationToken)
    {
        var ragMetadata = GetRagMetadata(resource);

        if (ragMetadata?.IsInScope != true)
        {
            return;
        }

        string templateId;
        Dictionary<string, object> arguments = null;

        if (hasReferences)
        {
            templateId = AITemplateIds.RagScopeWithRefs;
        }
        else if (turnContext.DisableTools)
        {
            templateId = AITemplateIds.RagScopeNoRefsToolsDisabled;
        }
        else
        {
            templateId = AITemplateIds.RagScopeNoRefsToolsEnabled;
            arguments = new Dictionary<string, object>
            {
                ["searchToolNames"] = new[] { SystemToolNames.SearchDataSources, SystemToolNames.SearchDocuments },
            };
        }

        var guidance = await _templateService.RenderAsync(templateId, arguments, cancellationToken);

        if (!string.IsNullOrWhiteSpace(guidance))
        {
            turnContext.SystemMessageBuilder.AppendLine();
            turnContext.SystemMessageBuilder.AppendLine(guidance);
        }

        if (!hasReferences && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Realtime retrieval found nothing for an in-scope profile; the model is instructed to stay within the knowledge base rather than answer from its own knowledge.");
        }
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

    /// <summary>
    /// Builds the per-turn context the preemptive handlers run against. It deliberately shares the session's
    /// <see cref="AICompletionContext"/> by reference — that is what carries the data source id, the attached
    /// documents and the tool state the handlers read — while keeping its own accumulator so the retrieved block
    /// belongs to this turn alone.
    /// </summary>
    /// <param name="context">The prepared session context.</param>
    /// <param name="utterance">The transcript of the user's utterance.</param>
    private static OrchestrationContext CreateTurnContext(OrchestrationContext context, string utterance)
    {
        return new OrchestrationContext
        {
            UserMessage = utterance,
            CompletionContext = context.CompletionContext,
            ServiceProvider = context.ServiceProvider,
            SourceName = context.SourceName,
            Documents = context.Documents,
            DisableTools = context.DisableTools,
            ExecutionMode = OrchestrationExecutionMode.Realtime,
        };
    }

    /// <summary>
    /// Copies the citations the handlers produced onto the ambient invocation scope. Preemptive handlers publish
    /// them on the orchestration context, which the text path reads after the completion; a realtime turn has no
    /// such hand-off point and reads its citations from the scope instead — the same place tool-driven citations
    /// already land, so a grounded turn cites its sources like any other.
    /// </summary>
    /// <param name="turnContext">The per-turn context the handlers wrote to.</param>
    /// <returns>The number of citations promoted.</returns>
    private static int PromoteReferences(OrchestrationContext turnContext)
    {
        var invocation = AIInvocationScope.Current;

        if (invocation is null)
        {
            return 0;
        }

        var promoted = 0;

        foreach (var key in _referencePropertyKeys)
        {
            if (turnContext.Properties.TryGetValue(key, out var value) &&
                value is Dictionary<string, AICompletionReference> references)
            {
                foreach (var (marker, reference) in references)
                {
                    invocation.ToolReferences[marker] = reference;
                    promoted++;
                }
            }
        }

        return promoted;
    }
}
