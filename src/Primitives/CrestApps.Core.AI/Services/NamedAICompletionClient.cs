using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Exceptions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

public abstract class NamedAICompletionClient : AICompletionServiceBase, IAICompletionClient
{
    public const string DefaultLogCategory = "AICompletionService";

    private readonly IAIClientFactory _aIClientFactory;
    private readonly IDistributedCache _distributedCache;
    private readonly IEnumerable<IAICompletionServiceHandler> _handlers;
    private readonly DefaultAIOptions _defaultOptions;
    private readonly IServiceProvider _serviceProvider;

    protected readonly ILogger Logger;
    protected readonly ILoggerFactory LoggerFactory;

    protected NamedAICompletionClient(
        string clientName,
        IAIClientFactory aIClientFactory,
        IDistributedCache distributedCache,
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        DefaultAIOptions defaultOptions,
        IEnumerable<IAICompletionServiceHandler> handlers,
        ITemplateService aiTemplateService,
        IAIDeploymentManager deploymentManager)
        : base(aiTemplateService, deploymentManager)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);

        ClientName = clientName;
        _aIClientFactory = aIClientFactory;
        _distributedCache = distributedCache;
        LoggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
        _defaultOptions = defaultOptions;
        Logger = loggerFactory.CreateLogger(DefaultLogCategory);
        _handlers = handlers;
    }

    public string ClientName { get; }

    protected virtual ValueTask ConfigureChatOptionsAsync(CompletionServiceConfigureContext configureContext)
    {
        return ValueTask.CompletedTask;
    }

    protected virtual void ConfigureFunctionInvocation(FunctionInvokingChatClient client)
    {
        client.MaximumIterationsPerRequest = _defaultOptions.MaximumIterationsPerRequest;
    }

    protected virtual bool SupportFunctionInvocation(AICompletionContext context, string modelName)
    {
        return !context.DisableTools;
    }

    protected virtual void ConfigureLogger(LoggingChatClient client)
    {
    }

    protected virtual void ConfigureOpenTelemetry(OpenTelemetryChatClient client)
    {
    }

    protected virtual void ProcessChatResponseUpdate(ChatResponseUpdate update, IEnumerable<ChatMessage> prompts)
    {
    }

    protected virtual void ProcessChatResponse(ChatResponse response, IEnumerable<ChatMessage> prompts)
    {
    }

    public async Task<ChatResponse> CompleteAsync(IEnumerable<ChatMessage> messages, AICompletionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(context);

        // Use the deployment resolver with fallback to legacy dictionary-based resolution.
        var deployment = await ResolveDeploymentAsync(
            AIDeploymentType.Chat,
            ClientName,
            deploymentName: context.ChatDeploymentName);

        if (deployment == null)
        {
            Logger.LogWarning("Unable to chat. Unable to find a deployment and no fallback deployment could be resolved.");

            throw new AIDeploymentNotFoundException("Unable to resolve a chat deployment for the current request.");
        }

        if (string.IsNullOrEmpty(deployment.ModelName))
        {
            Logger.LogWarning("Unable to chat. Unable to find a deployment name '{DeploymentName}' or the default deployment", context.ChatDeploymentName);

            throw new AIDeploymentConfigurationException("The resolved chat deployment is missing a model name.");
        }

        try
        {
            var chatOptions = await GetChatOptionsAsync(context, deployment.ModelName, false);

            var chatClient = await BuildClientAsync(deployment, context, chatOptions);

            var prompts = GetPrompts(messages, context);

            var response = await chatClient.GetResponseAsync(prompts, chatOptions, cancellationToken);

            ProcessChatResponse(response, prompts);

            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "An error occurred while chatting with the {Name} service.", ClientName);
        }

        return null;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingAsync(IEnumerable<ChatMessage> messages, AICompletionContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(context);

        // Use the deployment resolver with fallback to legacy dictionary-based resolution.
        var deployment = await ResolveDeploymentAsync(
            AIDeploymentType.Chat,
            ClientName,
            deploymentName: context.ChatDeploymentName);

        if (deployment == null)
        {
            Logger.LogWarning("Unable to chat. Unable to find a deployment and no fallback deployment could be resolved.");

            throw new AIDeploymentNotFoundException("Unable to resolve a chat deployment for the current request.");
        }

        if (string.IsNullOrEmpty(deployment.ModelName))
        {
            Logger.LogWarning("Unable to chat. Unable to find a deployment name '{DeploymentName}' or the default deployment", context.ChatDeploymentName);

            throw new AIDeploymentConfigurationException("The resolved chat deployment is missing a model name.");
        }

        var chatOptions = await GetChatOptionsAsync(context, deployment.ModelName, true);

        var chatClient = await BuildClientAsync(deployment, context, chatOptions);

        var prompts = GetPrompts(messages, context);

        await foreach (var update in chatClient.GetStreamingResponseAsync(prompts, chatOptions, cancellationToken))
        {
            ProcessChatResponseUpdate(update, prompts);

            yield return update;
        }
    }

    private static List<ChatMessage> GetPrompts(IEnumerable<ChatMessage> messages, AICompletionContext context)
    {
        var chatMessages = messages.Where(x => (x.Role == ChatRole.User || x.Role == ChatRole.Assistant) && !string.IsNullOrEmpty(x.Text));

        var prompts = new List<ChatMessage>();

        var systemMessage = context.SystemMessage;

        if (!string.IsNullOrEmpty(systemMessage))
        {
            prompts.Add(new ChatMessage(ChatRole.System, systemMessage));
        }

        if (context.PastMessagesCount > 1)
        {
            var skip = GetTotalMessagesToSkip(chatMessages.Count(), context.PastMessagesCount.Value);

            prompts.AddRange(chatMessages.Skip(skip).Take(context.PastMessagesCount.Value));
        }
        else
        {
            prompts.AddRange(chatMessages);
        }

        return prompts;
    }

    private async Task<ChatOptions> GetChatOptionsAsync(AICompletionContext context, string deploymentName, bool isStreaming)
    {
        var chatOptions = new ChatOptions()
        {
            Temperature = context.Temperature,
            TopP = context.TopP,
            FrequencyPenalty = context.FrequencyPenalty,
            PresencePenalty = context.PresencePenalty,
            MaxOutputTokens = context.MaxTokens,
        };

        var supportFunctions = SupportFunctionInvocation(context, deploymentName);

        var configureContext = new CompletionServiceConfigureContext(chatOptions, context, supportFunctions)
        {
            DeploymentName = deploymentName,
            ClientName = ClientName,
            ImplemenationName = ClientName,
            IsStreaming = isStreaming,
        };

        await _handlers.InvokeHandlersAsync((handler, ctx) => handler.ConfigureAsync(ctx), configureContext, Logger);

        if (!supportFunctions || (chatOptions.Tools is not null && chatOptions.Tools.Count == 0))
        {
            chatOptions.Tools = null;
        }

        await ConfigureChatOptionsAsync(configureContext);

        chatOptions.AddUsageTracking(context, clientName: ClientName);

        return chatOptions;
    }

    private async ValueTask<IChatClient> BuildClientAsync(AIDeployment deployment, AICompletionContext context, ChatOptions chatOptions)
    {
        var client = await _aIClientFactory.CreateChatClientAsync(deployment);

        var builder = new ChatClientBuilder(client);

        builder.UseLogging(LoggerFactory, ConfigureLogger);

        if (SupportFunctionInvocation(context, deployment.ModelName))
        {
            builder.UseFunctionInvocation(LoggerFactory, ConfigureFunctionInvocation);
        }

        // Tool-enabled requests can carry non-serializable delegate metadata in the tool graph,
        // which the distributed cache layer hashes as part of ChatOptions.
        if (_defaultOptions.EnableDistributedCaching && context.UseCaching && !HasCacheUnsafeTools(chatOptions))
        {
            builder.UseDistributedCache(_distributedCache);
        }

        if (_defaultOptions.EnableOpenTelemetry)
        {
            builder.UseOpenTelemetry(LoggerFactory, sourceName: DefaultLogCategory, ConfigureOpenTelemetry);
        }

        return builder.Build(_serviceProvider);
    }

    private static bool HasCacheUnsafeTools(ChatOptions chatOptions)
    {
        return chatOptions.Tools is { Count: > 0 };
    }
}
