using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure.AI.OpenAI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Azure.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Templates.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace CrestApps.Core.AI.OpenAI.Azure.Services;

public sealed class AzureOpenAICompletionClient : AICompletionServiceBase, IAICompletionClient
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IEnumerable<IAICompletionServiceHandler> _completionServiceHandlers;
    private readonly IEnumerable<IAIProviderConnectionHandler> _connectionHandlers;
    private readonly DefaultAIOptions _defaultOptions;
    private readonly IAIProviderConnectionStore _connectionStore;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly AzureClientOptions _azureClientOptions;
    private readonly ILogger _logger;

    public AzureOpenAICompletionClient(
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        IEnumerable<IAICompletionServiceHandler> completionServiceHandlers,
        IEnumerable<IAIProviderConnectionHandler> connectionHandlers,
        DefaultAIOptions defaultOptions,
        ITemplateService aiTemplateService,
        IAIDeploymentManager deploymentManager,
        IAIProviderConnectionStore connectionStore,
        IDataProtectionProvider dataProtectionProvider,
        IOptionsSnapshot<AzureClientOptions> azureClientOptions)
        : base(aiTemplateService, deploymentManager)
    {
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
        _completionServiceHandlers = completionServiceHandlers;
        _connectionHandlers = connectionHandlers;
        _defaultOptions = defaultOptions;
        _connectionStore = connectionStore;
        _dataProtectionProvider = dataProtectionProvider;
        _azureClientOptions = azureClientOptions.Value;
        _logger = loggerFactory.CreateLogger<AzureOpenAICompletionClient>();
    }

    public string ClientName
    {
        get
        {
            return AzureOpenAIConstants.ClientName;
        }
    }

    public async Task<Microsoft.Extensions.AI.ChatResponse> CompleteAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, AICompletionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(context);

        var (deployment, connectionProperties) = await ResolveChatConfigurationAsync(context.ChatDeploymentName);
        if (deployment == null || connectionProperties == null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(deployment.ModelName))
        {
            _logger.LogWarning("Unable to chat. Unable to find a deployment name '{DeploymentName}' or the default deployment", context.ChatDeploymentName);
            return null;
        }

        var azureMessages = new List<ChatMessage>();
        var currentPrompt = string.Empty;
        foreach (var message in messages)
        {
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                continue;
            }

            if (message.Role == Microsoft.Extensions.AI.ChatRole.User)
            {
                azureMessages.Add(new UserChatMessage(message.Text));
                currentPrompt = message.Text;
            }
            else if (message.Role == Microsoft.Extensions.AI.ChatRole.Assistant)
            {
                azureMessages.Add(new AssistantChatMessage(message.Text));
            }
        }

        var prompts = GetPrompts(context, azureMessages);
        var connectionName = deployment.ConnectionName;
        var azureClient = GetChatClient(connectionProperties);
        var chatClient = azureClient.GetChatClient(deployment.ModelName);
        var functions = await ResolveToolsAsync(context, deployment.ModelName);
        var chatOptions = GetOptions(context, functions);
        var systemFunctions = await ConfigureOptionsAsync(chatOptions, context, prompts);
        var allFunctions = systemFunctions.Count > 0 ? functions.Concat(systemFunctions) : functions;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var data = await chatClient.CompleteChatAsync(prompts, chatOptions, cancellationToken);
            if (data is null)
            {
                return null;
            }

            var iterations = 0;
            while (data.Value.FinishReason == ChatFinishReason.ToolCalls && iterations < _defaultOptions.MaximumIterationsPerRequest)
            {
                await ProcessToolCallsAsync(prompts, data.Value.ToolCalls, allFunctions);
                // Create a new chat option that excludes references to data sources to address the limitations in Azure OpenAI.
                data = await chatClient.CompleteChatAsync(prompts, GetOptions(context, allFunctions), cancellationToken);
                iterations++;
            }

            var role = new Microsoft.Extensions.AI.ChatRole(data.Value.Role.ToString().ToLowerInvariant());
            var choices = new List<Microsoft.Extensions.AI.ChatMessage>();
            foreach (var choice in data.Value.Content)
            {
                choices.Add(new Microsoft.Extensions.AI.ChatMessage(role, choice.Text));
            }

            // Notify the user when the maximum iteration limit was reached while the model still wanted to make tool calls.
            if (iterations >= _defaultOptions.MaximumIterationsPerRequest && data.Value.FinishReason == ChatFinishReason.ToolCalls)
            {
                choices.Add(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "\n\n⚠️ The operation reached the maximum number of tool-call iterations and may be incomplete. " + "Please try again or break the task into smaller steps."));
            }

            var result = new Microsoft.Extensions.AI.ChatResponse(choices)
            {
                ResponseId = data.Value.Id,
                CreatedAt = data.Value.CreatedAt,
                ModelId = data.Value.Model,
                FinishReason = new Microsoft.Extensions.AI.ChatFinishReason(data.Value.FinishReason.ToString()),
                Usage = new Microsoft.Extensions.AI.UsageDetails()
                {
                    InputTokenCount = data.Value.Usage.InputTokenCount,
                    OutputTokenCount = data.Value.Usage.OutputTokenCount,
                    TotalTokenCount = data.Value.Usage.TotalTokenCount,
                },
            };
            stopwatch.Stop();
            await RecordUsageAsync(context, connectionName, deployment.ModelName, result.ModelId, result.ResponseId, result.Usage, stopwatch.Elapsed.TotalMilliseconds, false, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to get chat completion result from Azure OpenAI.");
        }

        return null;
    }

    public async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> CompleteStreamingAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, AICompletionContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(context);

        var (deployment, connection) = await ResolveChatConfigurationAsync(context.ChatDeploymentName);
        if (deployment == null || connection == null)
        {
            yield break;
        }

        if (string.IsNullOrEmpty(deployment.ModelName))
        {
            _logger.LogWarning("Unable to chat. Unable to find a deployment name '{DeploymentName}' or the default deployment", context.ChatDeploymentName);
            yield break;
        }

        var azureMessages = new List<ChatMessage>();
        string currentPrompt;

        foreach (var message in messages)
        {
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                continue;
            }

            if (message.Role == Microsoft.Extensions.AI.ChatRole.User)
            {
                azureMessages.Add(new UserChatMessage(message.Text));
                currentPrompt = message.Text;
            }
            else if (message.Role == Microsoft.Extensions.AI.ChatRole.Assistant)
            {
                azureMessages.Add(new AssistantChatMessage(message.Text));
            }
        }

        var connectionName = deployment.ConnectionName;
        var azureClient = GetChatClient(connection);
        var chatClient = azureClient.GetChatClient(deployment.ModelName);
        var functions = await ResolveToolsAsync(context, deployment.ModelName);
        var chatOptions = GetOptions(context, functions);
        ChatCompletionOptions subSequenceContext = null;
        var prompts = GetPrompts(context, azureMessages);
        var systemFunctions = await ConfigureOptionsAsync(chatOptions, context, prompts);
        var allFunctions = systemFunctions.Count > 0 ? functions.Concat(systemFunctions) : functions;
        // Accumulate tool call updates across streaming chunks.
        // Key is the tool call index, value contains the accumulated tool call data.
        var accumulatedToolCalls = new Dictionary<int, (string ToolCallId, string FunctionName, List<byte> ArgumentBytes)>();
        var iterations = 0;
        var stopwatch = Stopwatch.StartNew();
        Microsoft.Extensions.AI.UsageDetails usage = null;
        string responseId = null;
        string modelId = null;

        while (iterations <= _defaultOptions.MaximumIterationsPerRequest)
        {
            var hasToolCalls = false;
            await foreach (var update in chatClient.CompleteChatStreamingAsync(prompts, chatOptions, cancellationToken))
            {
                // Accumulate tool call updates as they arrive.
                foreach (var toolCallUpdate in update.ToolCallUpdates)
                {
                    if (!accumulatedToolCalls.TryGetValue(toolCallUpdate.Index, out var accumulated))
                    {
                        accumulated = (toolCallUpdate.ToolCallId, toolCallUpdate.FunctionName, new List<byte>());
                        accumulatedToolCalls[toolCallUpdate.Index] = accumulated;
                    }

                    // Update ToolCallId and FunctionName if they are provided in this chunk.
                    if (!string.IsNullOrEmpty(toolCallUpdate.ToolCallId))
                    {
                        accumulated.ToolCallId = toolCallUpdate.ToolCallId;
                    }

                    if (!string.IsNullOrEmpty(toolCallUpdate.FunctionName))
                    {
                        accumulated.FunctionName = toolCallUpdate.FunctionName;
                    }

                    // Append function arguments bytes.
                    if (toolCallUpdate.FunctionArgumentsUpdate is not null)
                    {
                        accumulated.ArgumentBytes.AddRange(toolCallUpdate.FunctionArgumentsUpdate.ToArray());
                    }

                    accumulatedToolCalls[toolCallUpdate.Index] = accumulated;
                }

                if (update.FinishReason == ChatFinishReason.ToolCalls)
                {
                    // Convert accumulated tool call data to ChatToolCall objects.
                    var toolCalls = accumulatedToolCalls.Values.Where(tc => !string.IsNullOrEmpty(tc.ToolCallId) && !string.IsNullOrEmpty(tc.FunctionName)).Select(tc => ChatToolCall.CreateFunctionToolCall(tc.ToolCallId, tc.FunctionName, BinaryData.FromBytes(tc.ArgumentBytes.ToArray()))).ToList();
                    await ProcessToolCallsAsync(prompts, toolCalls, allFunctions);
                    // Clear accumulated tool calls for the next iteration.
                    accumulatedToolCalls.Clear();
                    // Create a new chat option that excludes references to data sources to address the limitations in Azure OpenAI.
                    chatOptions = subSequenceContext ??= GetOptions(context, allFunctions);
                    hasToolCalls = true;
                    iterations++;
                    break;
                }
                else
                {
                    var result = new Microsoft.Extensions.AI.ChatResponseUpdate
                    {
                        ResponseId = update.CompletionId,
                        CreatedAt = update.CreatedAt,
                        ModelId = update.Model,
                        Contents = update.ContentUpdate.Select(x => new Microsoft.Extensions.AI.TextContent(x.Text)).Cast<Microsoft.Extensions.AI.AIContent>().ToList(),
                    };
                    if (update.FinishReason is not null)
                    {
                        result.FinishReason = new Microsoft.Extensions.AI.ChatFinishReason(update.FinishReason?.ToString());
                    }

                    if (update.Role is not null)
                    {
                        result.Role = new Microsoft.Extensions.AI.ChatRole(update.Role.ToString().ToLowerInvariant());
                    }

                    responseId ??= result.ResponseId;
                    modelId ??= result.ModelId;
                    if (update.Usage is not null)
                    {
                        usage = new Microsoft.Extensions.AI.UsageDetails
                        {
                            InputTokenCount = update.Usage.InputTokenCount,
                            OutputTokenCount = update.Usage.OutputTokenCount,
                            TotalTokenCount = update.Usage.TotalTokenCount,
                        };
                    }

                    yield return result;
                }
            }

            if (!hasToolCalls)
            {
                break;
            }
        }

        stopwatch.Stop();
        await RecordUsageAsync(context, connectionName, deployment.ModelName, modelId, responseId, usage, stopwatch.Elapsed.TotalMilliseconds, true, cancellationToken);
        // Notify the user when the maximum iteration limit was reached while the model still wanted to make tool calls.
        if (iterations > _defaultOptions.MaximumIterationsPerRequest)
        {
            yield return new Microsoft.Extensions.AI.ChatResponseUpdate
            {
                Contents = [new Microsoft.Extensions.AI.TextContent("""
                    The operation reached the maximum number of tool-call iterations and may be incomplete.
                    Please try again or break the task into smaller steps.
                    """)],
            };
        }
    }

    private async Task ProcessToolCallsAsync(List<ChatMessage> prompts, IEnumerable<ChatToolCall> toolCalls, IEnumerable<Microsoft.Extensions.AI.AIFunction> functions)
    {
        if (toolCalls is null || !toolCalls.Any())
        {
            return;
        }

        prompts.Add(ChatMessage.CreateAssistantMessage(toolCalls));
        foreach (var toolCall in toolCalls)
        {
            var function = functions.FirstOrDefault(x => x.Name == toolCall.FunctionName);
            if (function is null)
            {
                prompts.Add(new ToolChatMessage(toolCall.Id, JsonSerializer.Serialize(new { error = $"Function '{toolCall.FunctionName}' not found." })));
                continue;
            }

            Microsoft.Extensions.AI.AIFunctionArguments arguments;
            try
            {
                arguments = toolCall.FunctionArguments.ToObjectFromJson<Microsoft.Extensions.AI.AIFunctionArguments>();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse arguments for tool call '{FunctionName}'. The model may have generated malformed JSON.", toolCall.FunctionName);
                // Detect truncation: "end of data" in the message indicates the model's output
                // was cut off (hit the output token limit) rather than being structurally wrong.
                var errorMessage = ex.Message.Contains("end of data", StringComparison.OrdinalIgnoreCase) ? """
The function arguments were truncated because the response exceeded the output token limit.
Please significantly reduce the size of the arguments. For content creation, use much shorter text,
omit optional fields, or split the operation into multiple smaller calls.
""" : "Invalid JSON in function arguments. Please fix the JSON structure and try again.";
                prompts.Add(new ToolChatMessage(toolCall.Id, JsonSerializer.Serialize(new { error = errorMessage })));
                continue;
            }

            arguments.Services = _serviceProvider;
            try
            {
                var result = await function.InvokeAsync(arguments);
                if (result is string str)
                {
                    prompts.Add(new ToolChatMessage(toolCall.Id, str));
                }
                else if (result is JsonElement element)
                {
                    prompts.Add(new ToolChatMessage(toolCall.Id, element.ToString()));
                }
                else
                {
                    var resultJson = JsonSerializer.Serialize(result);
                    prompts.Add(new ToolChatMessage(toolCall.Id, resultJson));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking function '{FunctionName}'.", toolCall.FunctionName);
                prompts.Add(new ToolChatMessage(toolCall.Id, JsonSerializer.Serialize(new { error = "Error invoking function." })));
            }
        }
    }

    private async ValueTask<(AIDeployment deployment, AIProviderConnectionEntry connection)> ResolveChatConfigurationAsync(string deploymentName)
    {
        var deployment = await ResolveDeploymentAsync(AIDeploymentType.Chat, AzureOpenAIConstants.ClientName, deploymentName: deploymentName);

        if (deployment == null)
        {
            _logger.LogWarning("Unable to chat. Unable to find a deployment named '{DeploymentName}' or a default Azure deployment.", deploymentName);
            return (null, null);
        }

        var connection = await ResolveConnectionAsync(deployment);

        if (connection == null)
        {
            _logger.LogWarning("Unable to chat. Unable to find a valid connection for Azure deployment '{DeploymentName}'.", deployment.Name);
            return (deployment, null);
        }

        return (deployment, connection);
    }

    private async ValueTask<AIProviderConnectionEntry> ResolveConnectionAsync(AIDeployment deployment)
    {
        if (!string.IsNullOrEmpty(deployment.ConnectionName))
        {
            var connection = await _connectionStore.GetAsync(deployment.ConnectionName, deployment.ClientName);
            if (connection == null)
            {
                return null;
            }

            return AIProviderConnectionEntryFactory.Create(connection, _connectionHandlers);
        }

        return AIDeploymentConnectionEntryFactory.Create(deployment, _dataProtectionProvider);
    }

    private AzureOpenAIClient GetChatClient(AIProviderConnectionEntry connection)
    {
        return AzureOpenAIClientFactory.Create(connection, _loggerFactory, _azureClientOptions);
    }

    private static async ValueTask<IReadOnlyList<Microsoft.Extensions.AI.AIFunction>> ConfigureOptionsAsync(ChatCompletionOptions chatOptions, AICompletionContext context, List<ChatMessage> prompts)
    {
        var optionsContext = new AzureOpenAIChatOptionsContext(chatOptions, context, prompts);

        if (optionsContext.SystemFunctions.Count > 0)
        {
            foreach (var function in optionsContext.SystemFunctions)
            {
                chatOptions.Tools.Add(function.ToChatTool());
            }

            if (chatOptions.Tools.Count > 0)
            {
                chatOptions.ToolChoice = ChatToolChoice.CreateAutoChoice();
            }
        }

        return optionsContext.SystemFunctions;
    }

    private static ChatCompletionOptions GetOptions(AICompletionContext context, IEnumerable<Microsoft.Extensions.AI.AIFunction> functions)
    {
        var chatOptions = new ChatCompletionOptions()
        {
            Temperature = context.Temperature,
            TopP = context.TopP,
            FrequencyPenalty = context.FrequencyPenalty,
            PresencePenalty = context.PresencePenalty,
            MaxOutputTokenCount = context.MaxTokens,
        };

        if (!context.DisableTools)
        {
            foreach (var function in functions)
            {
                chatOptions.Tools.Add(function.ToChatTool());
            }

            if (chatOptions.Tools.Count > 0)
            {
                chatOptions.ToolChoice = ChatToolChoice.CreateAutoChoice();
            }
        }

        return chatOptions;
    }

    private async Task<IEnumerable<Microsoft.Extensions.AI.AIFunction>> ResolveToolsAsync(AICompletionContext context, string deploymentName)
    {
        if (context.DisableTools)
        {
            return [];
        }

        // Use the same handler pipeline as NamedAICompletionClient to resolve tools.
        // This ensures authorization checks and consistent tool resolution across all clients.
        var chatOptions = new Microsoft.Extensions.AI.ChatOptions();
        var configureContext = new CompletionServiceConfigureContext(chatOptions, context, isFunctionInvocationSupported: true)
        {
            DeploymentName = deploymentName,
            ClientName = ClientName,
            ImplemenationName = ClientName,
            IsStreaming = false,
        };

        foreach (var handler in _completionServiceHandlers)
        {
            await handler.ConfigureAsync(configureContext);
        }

        if (chatOptions.Tools is null || chatOptions.Tools.Count == 0)
        {
            return [];
        }

        return chatOptions.Tools.OfType<Microsoft.Extensions.AI.AIFunction>().ToList();
    }

    private static List<ChatMessage> GetPrompts(AICompletionContext context, List<ChatMessage> azureMessages)
    {
        var prompts = new List<ChatMessage>();
        var systemMessage = context.SystemMessage;
        if (!string.IsNullOrEmpty(systemMessage))
        {
            prompts.Add(new SystemChatMessage(systemMessage));
        }

        if (context.PastMessagesCount > 1)
        {
            var skip = GetTotalMessagesToSkip(azureMessages.Count, context.PastMessagesCount.Value);
            prompts.AddRange(azureMessages.Skip(skip).Take(context.PastMessagesCount.Value));
        }
        else
        {
            prompts.AddRange(azureMessages);
        }

        return prompts;
    }

    private async Task RecordUsageAsync(AICompletionContext context, string connectionName, string deploymentName, string modelName, string responseId, Microsoft.Extensions.AI.UsageDetails usage, double responseLatencyMs, bool isStreaming, CancellationToken cancellationToken)
    {
        var observers = _serviceProvider.GetServices<IAICompletionUsageObserver>();

        if (!observers.Any())
        {
            return;
        }

        var record = AICompletionUsageRecordFactory.Create(context, ClientName, connectionName, deploymentName, modelName, responseId, usage?.InputTokenCount ?? 0, usage?.OutputTokenCount ?? 0, usage?.TotalTokenCount ?? 0, responseLatencyMs, isStreaming);

        await observers.InvokeHandlersAsync((observer, usageRecord) => observer.UsageRecordedAsync(usageRecord, cancellationToken), record, _logger);
    }
}
