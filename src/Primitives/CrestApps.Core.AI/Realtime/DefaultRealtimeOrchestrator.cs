#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
using CrestApps.Core;
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default <see cref="IRealtimeOrchestrator"/>. Reuses the shared orchestration PREPARE pipeline to build
/// the system message, tools, and RAG guidance for a resource, then configures and opens a provider
/// realtime session whose function calls are resolved by the Microsoft.Extensions.AI function-invocation
/// middleware. The request service provider is handed to that middleware so invoked tools resolve their
/// dependencies and observe the ambient <see cref="AIInvocationScope"/> exactly as they do on the text path.
/// </summary>
public sealed class DefaultRealtimeOrchestrator : IRealtimeOrchestrator
{
    private readonly IOrchestrationContextBuilder _contextBuilder;
    private readonly IAIDeploymentCapabilityService _capabilityService;
    private readonly IOptionsMonitor<DefaultAIDeploymentSettings> _deploymentSettings;
    private readonly IAIClientFactory _clientFactory;
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolMaterializer _toolMaterializer;
    private readonly IRealtimeSessionConfigurator _sessionConfigurator;
    private readonly IRealtimeTurnGrounding _turnGrounding;
    private readonly ToolRelevanceScoper _toolScoper;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DefaultRealtimeOrchestrator> _logger;

    private const string DefaultVoice = "alloy";

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultRealtimeOrchestrator"/> class.
    /// </summary>
    public DefaultRealtimeOrchestrator(
        IOrchestrationContextBuilder contextBuilder,
        IAIDeploymentCapabilityService capabilityService,
        IOptionsMonitor<DefaultAIDeploymentSettings> deploymentSettings,
        IAIClientFactory clientFactory,
        IToolRegistry toolRegistry,
        IToolMaterializer toolMaterializer,
        IRealtimeSessionConfigurator sessionConfigurator,
        IRealtimeTurnGrounding turnGrounding,
        ToolRelevanceScoper toolScoper,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        ILogger<DefaultRealtimeOrchestrator> logger)
    {
        _contextBuilder = contextBuilder;
        _capabilityService = capabilityService;
        _deploymentSettings = deploymentSettings;
        _clientFactory = clientFactory;
        _toolRegistry = toolRegistry;
        _toolMaterializer = toolMaterializer;
        _sessionConfigurator = sessionConfigurator;
        _turnGrounding = turnGrounding;
        _toolScoper = toolScoper;
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IRealtimeConversation> StartAsync(RealtimeOrchestrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Resource);

        // PREPARE: reuse the shared orchestration pipeline. There is no up-front user message in a live audio
        // session, so the shared preemptive RAG handler self-skips and RealtimeRagGuidanceHandler adds
        // search-tool guidance. Retrieval instead runs per turn through IRealtimeTurnGrounding, once the
        // provider has transcribed what the user actually said.
        var context = await _contextBuilder.BuildAsync(
            request.Resource,
            ctx =>
            {
                ctx.ExecutionMode = OrchestrationExecutionMode.Realtime;
                ctx.ConversationHistory = [];

                // Expose the chat session so document-aware handlers (RAG search, tabular) discover the
                // session-attached documents during PREPARE, exactly as the text path does in
                // AIChatResponseHandler. Chat interactions carry their documents on the resource itself.
                if (request.ChatSession is not null && ctx.CompletionContext is not null)
                {
                    ctx.CompletionContext.AdditionalProperties[AICompletionContextKeys.Session] = request.ChatSession;
                }

                request.ConfigureContext?.Invoke(ctx);

                // Decided here, before the BuiltAsync handlers run, because it changes what they should say:
                // a grounded session is handed its knowledge, so RealtimeRagGuidanceHandler must not also tell
                // the model to go and search for it.
                ctx.Properties[RealtimeOrchestrationContextKeys.GroundingEnabled] = _turnGrounding.IsGroundingAvailable(ctx, request.Resource);
            },
            cancellationToken);

        var hasInvocationScope = PopulateInvocationScope(context, request);

        var realtimeDeploymentName = string.IsNullOrWhiteSpace(request.RealtimeDeploymentName)
            ? _deploymentSettings.CurrentValue.DefaultRealtimeDeploymentName
            : request.RealtimeDeploymentName;

        var deployment = await _capabilityService.ResolveDeploymentWithFeatureAsync(AIDeploymentFeatureNames.Realtime, realtimeDeploymentName, cancellationToken)
            ?? throw new InvalidOperationException(
                "Unable to resolve a realtime deployment. Create a chat AI deployment whose model declares the 'realtime' capability.");

        var tools = await MaterializeToolsAsync(context, request.Resource, cancellationToken);

        // A session that advertises tools but has no ambient scope cannot execute a single one of them: every
        // call would return "requires an active AI execution context" as ordinary text, and the model would
        // relay that to the user as "I don't have that information". Fail at the door instead, where the cause
        // is unmistakable.
        if (!hasInvocationScope && tools.Count > 0)
        {
            throw new InvalidOperationException(
                "A realtime session was started with tools but without an active AIInvocationScope. Tools such as data source search read their context from that scope and would silently return errors. Call AIInvocationScope.Begin() before StartAsync and keep the scope alive for the whole session.");
        }

        // The fallback voice is an OpenAI voice name. A cascaded deployment speaks through whatever
        // provider its text-to-speech leg uses, where that name means nothing and would be rejected, so
        // leave the voice unset and let that provider fall back to its own configured default.
        var isCascaded = deployment.TryGet<CascadedRealtimeMetadata>(out var cascade) && cascade.IsComplete();

        await WarnWhenToolsCannotBeCalledAsync(deployment, isCascaded ? cascade : null, tools, cancellationToken);

        // Decided during PREPARE (see above), because the guidance handlers needed to know it too.
        var isGrounded = context.Properties.TryGetValue(RealtimeOrchestrationContextKeys.GroundingEnabled, out var grounding) && grounding is true;

        RealtimeSessionOptions ConfigureSession(bool grounded) => _sessionConfigurator.Configure(new RealtimeSessionConfiguratorContext
        {
            Model = deployment.ModelName,
            Instructions = context.CompletionContext?.SystemMessage,
            Voice = string.IsNullOrWhiteSpace(request.Voice) && !isCascaded ? DefaultVoice : request.Voice,
            Tools = tools,
            MaxOutputTokens = context.CompletionContext?.MaxTokens,
            SpeechLanguage = request.SpeechLanguage,
            ReplyLanguage = request.ReplyLanguage,
            SilenceDurationMs = request.SilenceDurationMs,
            VadThreshold = request.VadThreshold,
            AllowInterruption = request.AllowInterruption,
            CreateResponseAutomatically = !grounded,
        });

        var options = ConfigureSession(isGrounded);

        // Grounding is driven by the transcript of each utterance. Without input transcription that transcript
        // never arrives, so a grounded session would hold its reply forever and appear to have hung. Fall back to
        // the provider's automatic replies (tool-driven retrieval) rather than going silent.
        if (isGrounded && options.TranscriptionOptions is null)
        {
            _logger.LogWarning(
                "Per-turn knowledge retrieval was requested for a realtime session that has no input-audio transcription configured. It has been disabled for this session; knowledge retrieval falls back to the search tool.");

            isGrounded = false;
            options = ConfigureSession(grounded: false);
        }

        var rawClient = await _clientFactory.CreateRealtimeClientAsync(deployment);

        // Wrap the provider client with the MEAI function-invocation middleware. The request service
        // provider becomes the tools' AIFunctionArguments.Services, matching the text path; AdditionalTools
        // guarantees the middleware can resolve every advertised tool for invocation.
        var client = rawClient
            .AsBuilder()
            .UseFunctionInvocation(_loggerFactory, invoker =>
            {
                if (tools.Count > 0)
                {
                    invoker.AdditionalTools = [.. tools];
                }
            })
            .Build(_serviceProvider);

        var session = await client.CreateSessionAsync(options, cancellationToken);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Started realtime session for resource '{ResourceType}' using deployment '{Deployment}' with {ToolCount} tool(s); per-turn knowledge retrieval enabled: {Grounded}.",
                request.Resource.GetType().Name, deployment.Name, tools.Count, isGrounded);
        }

        return new DefaultRealtimeConversation(
            session,
            isGrounded
                ? (utterance, token) => _turnGrounding.RetrieveAsync(context, request.Resource, utterance, token)
                : null);
    }

    /// <summary>
    /// Reports a session whose tools will never reach the model because the deployment that would call them does
    /// not declare the tool-calling feature. The enforcement that removes them lives deep in the chat pipeline and
    /// says nothing about realtime, so without this the session looks healthy while every knowledge question is
    /// answered from the model's own memory.
    /// </summary>
    /// <param name="deployment">The resolved realtime deployment.</param>
    /// <param name="cascade">The cascade metadata when the deployment chains other deployments; otherwise <see langword="null"/>.</param>
    /// <param name="tools">The tools resolved for the session.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task WarnWhenToolsCannotBeCalledAsync(
        AIDeployment deployment,
        CascadedRealtimeMetadata cascade,
        IReadOnlyList<AITool> tools,
        CancellationToken cancellationToken)
    {
        if (tools.Count == 0)
        {
            return;
        }

        // A cascade's tools are called by its chat leg, not by the cascade itself, so that is the deployment
        // whose declared features decide whether they survive.
        var toolDeploymentName = cascade is not null ? cascade.ChatDeploymentName : deployment.Name;

        if (string.IsNullOrWhiteSpace(toolDeploymentName))
        {
            return;
        }

        AIDeploymentCapabilities capabilities;

        try
        {
            capabilities = await _capabilityService.GetCapabilitiesAsync(toolDeploymentName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A diagnostic must never be the thing that stops a session from starting.
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "Could not read the capabilities of deployment '{Deployment}' to verify tool support.", toolDeploymentName);
            }

            return;
        }

        // Only a deployment that declares capability metadata can be judged. One that declares none is
        // unconstrained, and its tools are passed through untouched.
        if (capabilities is null || capabilities.SupportsFeature(AIDeploymentFeatureNames.ToolCalling))
        {
            return;
        }

        _logger.LogError(
            "Deployment '{Deployment}' does not declare the '{Feature}' feature, so the {ToolCount} tool(s) resolved for this realtime session — including knowledge base search — will be removed before the model sees them. Enable '{Feature}' on the model behind that deployment, or the assistant will answer knowledge questions from its own training data.",
            toolDeploymentName, AIDeploymentFeatureNames.ToolCalling, tools.Count, AIDeploymentFeatureNames.ToolCalling);
    }

    private async Task<IReadOnlyList<AITool>> MaterializeToolsAsync(OrchestrationContext context, object resource, CancellationToken cancellationToken)
    {
        if (context.DisableTools || context.CompletionContext is null)
        {
            return [];
        }

        // Realtime configures its tools once, at session open, and every one of them sits in the model's context
        // for every response of the conversation. The profile is the authorization boundary, as with AI Sessions,
        // so no per-user gate is applied.
        var entries = await _toolRegistry.GetAllAsync(context.CompletionContext, cancellationToken);

        if (entries.Count == 0)
        {
            return [];
        }

        // A tool another tool depends on must survive scoping, exactly as on the chat path.
        DefaultOrchestrator.MergeDependencyToolNames(context);

        // Past the same threshold at which chat stops passing tools through whole, trim to the ones relevant to
        // what this assistant is for. There is no user message yet to score against, so the profile's own
        // instructions stand in for one. Chat re-scopes per request; realtime cannot without deferring every
        // reply, so this one decision has to hold for the whole session — which is why it is a warning, not a
        // debug line: the fix is to curate the profile, not to rely on the cut.
        if (_toolScoper.ShouldScope(entries.Count))
        {
            var scoped = _toolScoper.Scope(context.CompletionContext.SystemMessage, context.MustIncludeTools, entries);
            var dropped = entries.Where(entry => !scoped.Contains(entry)).Select(entry => entry.Name).ToArray();

            _logger.LogWarning(
                "Realtime session for '{ResourceType}' resolved {Total} tool(s), above the {Threshold} a session carries well. Scoped to {Kept} by relevance to the profile's instructions; the model will not see: [{Dropped}]. Curate the profile's tools — or select fewer tools from its MCP connections — so this cut is not needed.",
                resource.GetType().Name,
                entries.Count, _toolScoper.ScopingThreshold, scoped.Count, string.Join(", ", dropped));

            entries = scoped;
        }

        var result = await _toolMaterializer.MaterializeAsync(entries, ToolMaterializationOptions.Default, cancellationToken);

        return result.Tools;
    }

    /// <returns><see langword="true"/> when an ambient invocation scope was present and populated.</returns>
    private bool PopulateInvocationScope(OrchestrationContext context, RealtimeOrchestrationRequest request)
    {
        // AIToolExecutionContextOrchestrationHandler already set ToolExecutionContext during BuildAsync
        // (it ran under this ambient scope). Fill in the remaining fields tools read, mirroring
        // AIChatResponseHandler so existing tools work unchanged.
        var invocation = AIInvocationScope.Current;

        if (invocation is null)
        {
            // A tool-less session still works without a scope, so this is only fatal when tools were resolved —
            // the caller decides once it knows how many there are.
            _logger.LogWarning(
                "No AIInvocationScope is active when starting a realtime session. AI tools that rely on the ambient context (data source search, documents) will not function. Begin a scope before calling StartAsync.");

            return false;
        }

        invocation.CompletionContext = context.CompletionContext;
        invocation.DataSourceId = context.CompletionContext?.DataSourceId;

        if (request.ChatSession is not null)
        {
            invocation.ChatSession = request.ChatSession;
            invocation.Items[nameof(AIChatSession)] = request.ChatSession;
        }

        if (request.Interaction is not null)
        {
            invocation.ChatInteraction = request.Interaction;
        }

        return true;
    }
}
