using System.Runtime.CompilerServices;
using Anthropic.Models.Messages;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Claude.Services;

/// <summary>
/// Anthropic-powered orchestrator that runs Claude directly through the official C# SDK.
/// </summary>
public sealed class ClaudeOrchestrator : IOrchestrator
{
    public const string OrchestratorName = "anthropic";

    private readonly IToolRegistry _toolRegistry;
    private readonly IToolMaterializer _toolMaterializer;
    private readonly IUserAccessor _userAccessor;
    private readonly ClaudeClientService _clientService;
    private readonly IOptionsSnapshot<ClaudeOptions> _anthropicOptions;
    private readonly DefaultAIOptions _defaultOptions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ClaudeOrchestrator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaudeOrchestrator"/> class.
    /// </summary>
    /// <param name="toolRegistry">The tool registry.</param>
    /// <param name="toolMaterializer">The shared scoped-entries-to-tools materializer.</param>
    /// <param name="userAccessor">The accessor that resolves the principal owning the current request.</param>
    /// <param name="clientService">The client service.</param>
    /// <param name="anthropicOptions">The anthropic options.</param>
    /// <param name="defaultOptions">The default options.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="logger">The logger.</param>
    public ClaudeOrchestrator(
        IToolRegistry toolRegistry,
        IToolMaterializer toolMaterializer,
        IUserAccessor userAccessor,
        ClaudeClientService clientService,
        IOptionsSnapshot<ClaudeOptions> anthropicOptions,
        IOptions<DefaultAIOptions> defaultOptions,
        ILoggerFactory loggerFactory,
        ILogger<ClaudeOrchestrator> logger)
    {
        _toolRegistry = toolRegistry;
        _toolMaterializer = toolMaterializer;
        _userAccessor = userAccessor;
        _clientService = clientService;
        _anthropicOptions = anthropicOptions;
        _defaultOptions = defaultOptions.Value;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name => OrchestratorName;

    /// <summary>
    /// Executes streaming.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async IAsyncEnumerable<ChatResponseUpdate> ExecuteStreamingAsync(
        OrchestrationContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.CompletionContext);

        context.SourceName ??= Name;

        ClaudeSessionMetadata metadata = null;
        if (context.Properties.TryGetValue(nameof(ClaudeSessionMetadata), out var metadataValue))
        {
            metadata = metadataValue as ClaudeSessionMetadata;
        }

        var modelId = !string.IsNullOrWhiteSpace(metadata?.ClaudeModel)
            ? metadata.ClaudeModel
            : null;
        var effortLevel = metadata?.EffortLevel ?? ClaudeEffortLevel.None;
        var anthropicOptions = _anthropicOptions.Value;
        modelId ??= anthropicOptions.DefaultModel;

        if (!IsConfigured(anthropicOptions))
        {
            yield return CreateTextResponse("Claude is not configured and cannot be used until it has been configured.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            yield return CreateTextResponse("Claude is configured, but no default model or override has been selected.");
            yield break;
        }

        var scopedEntries = await _toolRegistry.GetAllAsync(context.CompletionContext, cancellationToken);
        context.CompletionContext.ToolNames = scopedEntries.Select(entry => entry.Name).ToArray();
        context.CompletionContext.AdditionalProperties[FunctionInvocationAICompletionServiceHandler.ScopedEntriesKey] = scopedEntries;

        using var client = _clientService.CreateClient();
        // The Anthropic client is created here rather than by the AI client factory, so it is metered here. It is
        // wrapped before the tool-calling loop is added, so every round trip is recorded as its own request.
        var chatClient = AIUsageMetering.Meter(
            client.AsIChatClient(modelId, context.CompletionContext.MaxTokens ?? _defaultOptions.MaxOutputTokens),
            OrchestratorName,
            null,
            modelId,
            context.ServiceProvider,
            AIUsagePurposes.Conversation);

        var builder = new ChatClientBuilder(chatClient);
        builder.UseLogging(_loggerFactory);
        builder.UseFunctionInvocation(_loggerFactory, invocation =>
        {
            invocation.MaximumIterationsPerRequest = _defaultOptions.MaximumIterationsPerRequest;
        });

        var configuredClient = builder.Build(context.ServiceProvider);
        var chatOptions = BuildChatOptions(context.CompletionContext, modelId, effortLevel);
        var prompts = BuildPrompts(context);

        await AddToolsAsync(context.CompletionContext, scopedEntries, chatOptions, cancellationToken);

        // Runs after the tools are added so the last tool gets its cache breakpoint.
        if (anthropicOptions.EnablePromptCaching)
        {
            ApplyPromptCaching(prompts, chatOptions);
        }

        ChatResponseUpdate errorResponse = null;

        await using var responseEnumerator = configuredClient
            .GetStreamingResponseAsync(prompts, chatOptions, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            ChatResponseUpdate update;

            try
            {
                if (!await responseEnumerator.MoveNextAsync())
                {
                    break;
                }

                update = responseEnumerator.Current;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "ClaudeOrchestrator: Unexpected error during Anthropic session.");
                errorResponse = CreateTextResponse("An unexpected error occurred while communicating with Anthropic. Please try again.");
                break;
            }

            yield return update;
        }

        if (errorResponse is not null)
        {
            yield return errorResponse;
        }
    }

    private static bool IsConfigured(ClaudeOptions options)
    {
        return options is not null &&
            !string.IsNullOrWhiteSpace(options.ApiKey) &&
            !string.IsNullOrWhiteSpace(options.DefaultModel);
    }

    /// <summary>
    /// Materializes the scoped tool entries into <see cref="ChatOptions.Tools"/>. The Anthropic chat client is called
    /// directly rather than through the AI completion service, so the function-invocation completion handler never
    /// runs for this orchestrator and the tools are added here the same way.
    /// </summary>
    /// <param name="completionContext">The completion context.</param>
    /// <param name="scopedEntries">The scoped tool entries.</param>
    /// <param name="chatOptions">The chat options to add the tools to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task AddToolsAsync(
        AICompletionContext completionContext,
        IReadOnlyList<ToolRegistryEntry> scopedEntries,
        ChatOptions chatOptions,
        CancellationToken cancellationToken)
    {
        if (completionContext.DisableTools || scopedEntries.Count == 0)
        {
            return;
        }

        // The per-user access check applies to Chat Interaction requests only. AI Sessions run the
        // profile exactly as configured (the profile is the authorization boundary) and may be
        // anonymous, so no session-time tool gate is applied.
        var isChatInteraction = completionContext.AdditionalProperties.ContainsKey(AICompletionContextKeys.Interaction);

        var result = await _toolMaterializer.MaterializeAsync(
            scopedEntries,
            new ToolMaterializationOptions
            {
                EnforceListableAccess = isChatInteraction,
                User = isChatInteraction ? _userAccessor.User : null,
            },
            cancellationToken);

        if (result.Tools.Count > 0)
        {
            chatOptions.Tools = [.. result.Tools];
        }

        if (result.DeniedToolNames.Count > 0)
        {
            _logger.LogWarning(
                "The current Chat Interaction caller is not authorized to use the following listable AI tools, which were excluded from the request: {ToolNames}.",
                string.Join(", ", result.DeniedToolNames));
        }
    }

    private static ChatOptions BuildChatOptions(AICompletionContext context, string modelId, ClaudeEffortLevel effortLevel)
    {
        var options = new ChatOptions
        {
            ModelId = modelId,
            Temperature = context.Temperature,
            TopP = context.TopP,
            FrequencyPenalty = context.FrequencyPenalty,
            PresencePenalty = context.PresencePenalty,
            MaxOutputTokens = context.MaxTokens,
        };

        if (effortLevel != ClaudeEffortLevel.None)
        {
            var effortValue = effortLevel switch
            {
                ClaudeEffortLevel.Low => "low",
                ClaudeEffortLevel.Medium => "medium",
                ClaudeEffortLevel.High => "high",
                _ => null,
            };

            if (effortValue is not null)
            {
                options.AdditionalProperties ??= [];
                options.AdditionalProperties["reasoning_effort"] = effortValue;
            }
        }

        return options;
    }

    private static List<ChatMessage> BuildPrompts(OrchestrationContext context)
    {
        var prompts = new List<ChatMessage>();
        var systemMessage = context.CompletionContext.SystemMessage;

        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            prompts.Add(new ChatMessage(ChatRole.System, systemMessage));
        }

        var conversationHistory = context.ConversationHistory;

        if (conversationHistory is not null)
        {
            var eligible = conversationHistory.Where(static message =>
                (message.Role == ChatRole.User || message.Role == ChatRole.Assistant) &&
                !string.IsNullOrWhiteSpace(message.Text));

            if (context.CompletionContext.PastMessagesCount > 0)
            {
                AddLastMessages(prompts, eligible, context.CompletionContext.PastMessagesCount.Value);
            }
            else
            {
                var materializedMessages = eligible.ToList();

                prompts.AddRange(materializedMessages);
            }
        }

        if (prompts.Count == 0 || prompts[^1].Text != context.UserMessage)
        {
            prompts.Add(new ChatMessage(ChatRole.User, context.UserMessage));
        }

        return prompts;
    }

    /// <summary>
    /// Adds the last <paramref name="count"/> eligible messages from a forward-only sequence in
    /// their original order without materializing the entire sequence.
    /// </summary>
    /// <param name="prompts">The destination prompt collection.</param>
    /// <param name="messages">The eligible messages.</param>
    /// <param name="count">The maximum number of trailing messages to add.</param>
    private static void AddLastMessages(
        List<ChatMessage> prompts,
        IEnumerable<ChatMessage> messages,
        int count)
    {
        var buffer = new List<ChatMessage>(Math.Min(count, 4));
        var nextIndex = 0;

        foreach (var message in messages)
        {
            if (buffer.Count < count)
            {
                buffer.Add(message);

                continue;
            }

            buffer[nextIndex] = message;
            nextIndex++;

            if (nextIndex == count)
            {
                nextIndex = 0;
            }
        }

        if (nextIndex == 0)
        {
            prompts.AddRange(buffer);

            return;
        }

        var requiredCapacity = (long)prompts.Count + buffer.Count;

        if (requiredCapacity <= int.MaxValue)
        {
            prompts.EnsureCapacity((int)requiredCapacity);
        }

        for (var index = nextIndex; index < buffer.Count; index++)
        {
            prompts.Add(buffer[index]);
        }

        for (var index = 0; index < nextIndex; index++)
        {
            prompts.Add(buffer[index]);
        }
    }

    /// <summary>
    /// Marks Anthropic prompt-cache breakpoints on the stable prefix of the request: the last tool definition,
    /// the end of the system prompt and the latest message. Anthropic caches everything up to and including a
    /// marked block, so later turns, and later round trips of the tool-calling loop, read that prefix at the
    /// cache-read rate. At most three of Anthropic's four breakpoints are used. A prefix shorter than the
    /// model's minimum cacheable length is sent uncached without error.
    /// </summary>
    /// <param name="prompts">The prompts to send. Marked messages are replaced by copies, so the caller's
    /// conversation history is never modified.</param>
    /// <param name="options">The chat options whose last function tool is marked.</param>
    private static void ApplyPromptCaching(List<ChatMessage> prompts, ChatOptions options)
    {
        if (options?.Tools is { Count: > 0 } tools)
        {
            for (var index = tools.Count - 1; index >= 0; index--)
            {
                if (tools[index] is AIFunction function)
                {
                    tools[index] = new CacheBreakpointAIFunction(function);

                    break;
                }
            }
        }

        if (prompts.Count == 0)
        {
            return;
        }

        if (prompts[0].Role == ChatRole.System)
        {
            prompts[0] = WithCacheBreakpoint(prompts[0]);

            if (prompts.Count == 1)
            {
                return;
            }
        }

        prompts[^1] = WithCacheBreakpoint(prompts[^1]);
    }

    private static ChatMessage WithCacheBreakpoint(ChatMessage message)
    {
        if (message.Contents.Count == 0 || message.Contents[^1] is not TextContent text)
        {
            return message;
        }

        var contents = new List<AIContent>(message.Contents)
        {
            [^1] = new TextContent(text.Text)
            {
                AdditionalProperties = text.AdditionalProperties?.Clone(),
            }.WithCacheControl(new CacheControlEphemeral()),
        };

        var copy = message.Clone();
        copy.Contents = contents;

        return copy;
    }

    private static ChatResponseUpdate CreateTextResponse(string text)
    {
        return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)],
        };
    }

    /// <summary>
    /// Wraps a tool so the Anthropic chat client places a prompt-cache breakpoint after its definition.
    /// </summary>
    private sealed class CacheBreakpointAIFunction : DelegatingAIFunction
    {
        private readonly Dictionary<string, object> _additionalProperties;

        public CacheBreakpointAIFunction(AIFunction innerFunction)
            : base(innerFunction)
        {
            _additionalProperties = new Dictionary<string, object>(innerFunction.AdditionalProperties)
            {
                [nameof(Tool.CacheControl)] = new CacheControlEphemeral(),
            };
        }

        public override IReadOnlyDictionary<string, object> AdditionalProperties => _additionalProperties;
    }
}
