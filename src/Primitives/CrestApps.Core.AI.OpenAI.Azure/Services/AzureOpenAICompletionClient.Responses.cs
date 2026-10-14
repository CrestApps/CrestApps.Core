using System.Runtime.CompilerServices;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.OpenAI.Services;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.OpenAI.Azure.Services;

public sealed partial class AzureOpenAICompletionClient
{
    /// <summary>
    /// Determines whether the request is sent to the Responses API instead of Chat Completions.
    /// </summary>
    /// <remarks>
    /// Reasoning models reject function tools combined with a reasoning effort on Chat Completions and accept that
    /// combination only on the Responses API. A connection whose <c>ChatApi</c> setting is <c>ChatCompletions</c> keeps
    /// every request on Chat Completions, for example in a region where the Responses API is not available yet.
    /// </remarks>
    /// <param name="connection">The connection that serves the request.</param>
    /// <param name="requestOptions">The resolved request options.</param>
    private bool UseResponsesApi(AIProviderConnectionEntry connection, AzureRequestOptions requestOptions)
    {
        if (requestOptions.Functions.Count == 0 || !OpenAIReasoningRoutingChatClient.RequiresResponsesApi(requestOptions.ResolvedOptions))
        {
            return false;
        }

        return OpenAIChatApis.GetChatApi(connection, _logger) != OpenAIChatApis.ChatCompletions;
    }

    private async Task<ChatResponse> CompleteWithResponsesApiAsync(IEnumerable<ChatMessage> messages, AICompletionContext context, AIDeployment deployment, AIProviderConnectionEntry connection, AzureRequestOptions requestOptions, CancellationToken cancellationToken)
    {
        try
        {
            var chatClient = await CreateResponsesChatClientAsync(deployment);

            return await chatClient.GetResponseAsync(GetResponsesPrompts(messages, context), GetResponsesOptions(context, requestOptions), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogCompletionFailure(ex, deployment, connection);
        }

        return null;
    }

    private async IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingWithResponsesApiAsync(IEnumerable<ChatMessage> messages, AICompletionContext context, AIDeployment deployment, AIProviderConnectionEntry connection, AzureRequestOptions requestOptions, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var chatClient = await CreateResponsesChatClientAsync(deployment);

        await using var updates = chatClient
            .GetStreamingResponseAsync(GetResponsesPrompts(messages, context), GetResponsesOptions(context, requestOptions), cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            try
            {
                if (!await updates.MoveNextAsync())
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogCompletionFailure(ex, deployment, connection);

                throw;
            }

            yield return updates.Current;
        }
    }

    /// <summary>
    /// Creates the client for a Responses API request.
    /// </summary>
    /// <remarks>
    /// The provider's chat client sends a request that carries function tools and a reasoning effort to the Responses
    /// API without storing the conversation, and the factory meters every round trip. Function invocation runs on top,
    /// so the tool calls of every round trip go to the Responses API as well.
    /// </remarks>
    /// <param name="deployment">The deployment that serves the request.</param>
    private ValueTask<IChatClient> CreateResponsesChatClientAsync(AIDeployment deployment)
    {
        return _clientFactory.CreateChatClientAsync(deployment, builder => builder.Use((innerClient, services) =>
        {
            var functionInvokingClient = new FunctionInvokingChatClient(innerClient, _loggerFactory, services)
            {
                MaximumIterationsPerRequest = _defaultOptions.MaximumIterationsPerRequest,
            };

            return functionInvokingClient.UseTextToolResults();
        }));
    }

    private static ChatOptions GetResponsesOptions(AICompletionContext context, AzureRequestOptions requestOptions)
    {
        var chatOptions = new ChatOptions
        {
            Temperature = context.Temperature,
            TopP = context.TopP,
            FrequencyPenalty = context.FrequencyPenalty,
            PresencePenalty = context.PresencePenalty,
            MaxOutputTokens = context.MaxTokens,
            Reasoning = requestOptions.ResolvedOptions.Reasoning,
            Tools = [.. requestOptions.Functions],
            ToolMode = ChatToolMode.Auto,
        };

        return chatOptions.AddUsageTracking(context, clientName: AzureOpenAIConstants.ClientName, defaultPurpose: AIUsagePurposes.Conversation);
    }

    /// <summary>
    /// Builds the Responses API prompts the way <see cref="AzureOpenAIChatMessageConverter"/> does for Chat Completions:
    /// the system message, then the user messages that carry content and the text of the assistant answers, limited to
    /// the configured number of past messages.
    /// </summary>
    /// <remarks>
    /// Only the text of an assistant message is kept. A stored function call without its result would make the
    /// Responses API reject the request.
    /// </remarks>
    /// <param name="messages">The conversation messages.</param>
    /// <param name="context">The completion context.</param>
    private static List<ChatMessage> GetResponsesPrompts(IEnumerable<ChatMessage> messages, AICompletionContext context)
    {
        var history = new List<ChatMessage>();

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.User && (!string.IsNullOrEmpty(message.Text) || message.Contents.Count > 0))
            {
                history.Add(message);
            }
            else if (message.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(message.Text))
            {
                history.Add(new ChatMessage(ChatRole.Assistant, message.Text));
            }
        }

        var prompts = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(context.SystemMessage))
        {
            prompts.Add(new ChatMessage(ChatRole.System, context.SystemMessage));
        }

        prompts.AddRange(context.PastMessagesCount > 1 ? history.TakeLast(context.PastMessagesCount.Value) : history);

        return prompts;
    }
}
