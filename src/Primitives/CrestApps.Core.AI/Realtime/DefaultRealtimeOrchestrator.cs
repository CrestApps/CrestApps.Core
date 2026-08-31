#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Clients;
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
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IRealtimeConversation> StartAsync(RealtimeOrchestrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Resource);

        // PREPARE: reuse the shared orchestration pipeline. There is no up-front user message in a live
        // audio session, so preemptive RAG self-skips; RealtimeRagGuidanceHandler adds search-tool guidance.
        var context = await _contextBuilder.BuildAsync(
            request.Resource,
            ctx =>
            {
                ctx.ExecutionMode = OrchestrationExecutionMode.Realtime;
                ctx.ConversationHistory = [];
                request.ConfigureContext?.Invoke(ctx);
            },
            cancellationToken);

        PopulateInvocationScope(context, request);

        var realtimeDeploymentName = string.IsNullOrWhiteSpace(request.RealtimeDeploymentName)
            ? _deploymentSettings.CurrentValue.DefaultRealtimeDeploymentName
            : request.RealtimeDeploymentName;

        var deployment = await _capabilityService.ResolveDeploymentWithFeatureAsync(AIModelFeatureNames.Realtime, realtimeDeploymentName, cancellationToken)
            ?? throw new InvalidOperationException(
                "Unable to resolve a realtime deployment. Create a chat AI deployment whose model declares the 'realtime' capability.");

        var tools = await MaterializeToolsAsync(context, cancellationToken);

        var options = _sessionConfigurator.Configure(new RealtimeSessionConfiguratorContext
        {
            Model = deployment.ModelName,
            Instructions = context.CompletionContext?.SystemMessage,
            Voice = string.IsNullOrWhiteSpace(request.Voice) ? DefaultVoice : request.Voice,
            Tools = tools,
            MaxOutputTokens = context.CompletionContext?.MaxTokens,
            SpeechLanguage = request.SpeechLanguage,
        });

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
                "Started realtime session for resource '{ResourceType}' using deployment '{Deployment}' with {ToolCount} tool(s).",
                request.Resource.GetType().Name, deployment.Name, tools.Count);
        }

        return new DefaultRealtimeConversation(session);
    }

    private async Task<IReadOnlyList<AITool>> MaterializeToolsAsync(OrchestrationContext context, CancellationToken cancellationToken)
    {
        if (context.DisableTools || context.CompletionContext is null)
        {
            return [];
        }

        // Realtime configures its tools once (no per-turn planning); inject the full resolved set.
        // The profile is the authorization boundary, as with AI Sessions, so no per-user gate is applied.
        var entries = await _toolRegistry.GetAllAsync(context.CompletionContext, cancellationToken);

        if (entries.Count == 0)
        {
            return [];
        }

        var result = await _toolMaterializer.MaterializeAsync(entries, ToolMaterializationOptions.Default, cancellationToken);

        return result.Tools;
    }

    private void PopulateInvocationScope(OrchestrationContext context, RealtimeOrchestrationRequest request)
    {
        // AIToolExecutionContextOrchestrationHandler already set ToolExecutionContext during BuildAsync
        // (it ran under this ambient scope). Fill in the remaining fields tools read, mirroring
        // AIChatResponseHandler so existing tools work unchanged.
        var invocation = AIInvocationScope.Current;

        if (invocation is null)
        {
            _logger.LogWarning(
                "No AIInvocationScope is active when starting a realtime session. AI tools that rely on the ambient context (data source search, documents) will not function. Begin a scope before calling StartAsync.");

            return;
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
    }
}
