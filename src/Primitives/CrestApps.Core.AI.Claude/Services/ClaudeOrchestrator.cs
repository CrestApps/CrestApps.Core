using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
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
    private readonly ClaudeClientService _clientService;
    private readonly IOptionsSnapshot<ClaudeOptions> _anthropicOptions;
    private readonly DefaultAIOptions _defaultOptions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ClaudeOrchestrator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaudeOrchestrator"/> class.
    /// </summary>
    /// <param name="toolRegistry">The tool registry.</param>
    /// <param name="clientService">The client service.</param>
    /// <param name="anthropicOptions">The anthropic options.</param>
    /// <param name="defaultOptions">The default options.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <param name="logger">The logger.</param>
    public ClaudeOrchestrator(
        IToolRegistry toolRegistry,
        ClaudeClientService clientService,
        IOptionsSnapshot<ClaudeOptions> anthropicOptions,
        IOptions<DefaultAIOptions> defaultOptions,
        ILoggerFactory loggerFactory,
        ILogger<ClaudeOrchestrator> logger)
    {
        _toolRegistry = toolRegistry;
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
        var chatClient = client.AsIChatClient(modelId, context.CompletionContext.MaxTokens ?? _defaultOptions.MaxOutputTokens);

        var builder = new ChatClientBuilder(chatClient);
        builder.UseLogging(_loggerFactory);
        builder.UseFunctionInvocation(_loggerFactory, invocation =>
        {
            invocation.MaximumIterationsPerRequest = _defaultOptions.MaximumIterationsPerRequest;
        });

        var configuredClient = builder.Build(context.ServiceProvider);
        var chatOptions = BuildChatOptions(context.CompletionContext, modelId, effortLevel);
        var prompts = BuildPrompts(context);
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

            if (context.CompletionContext.PastMessagesCount > 1)
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

    private static ChatResponseUpdate CreateTextResponse(string text)
    {
        return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)],
        };
    }
}
