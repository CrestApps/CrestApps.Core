using System.Diagnostics;
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
            var chatOptions = GetResponsesOptions(context, requestOptions);
            var response = await chatClient.GetResponseAsync(GetResponsesPrompts(messages, context), chatOptions, cancellationToken);

            foreach (var message in response.Messages)
            {
                ToolApprovalRequests.MarkCallsThatNeedNoApproval(message.Contents, chatOptions.Tools);
            }

            return response;
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
        var chatOptions = GetResponsesOptions(context, requestOptions);
        var startTimestamp = Stopwatch.GetTimestamp();
        var receivedText = false;

        await using var updates = chatClient
            .GetStreamingResponseAsync(GetResponsesPrompts(messages, context), chatOptions, cancellationToken)
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

            if (!receivedText && !string.IsNullOrEmpty(updates.Current.Text))
            {
                receivedText = true;

                AITelemetry.CompletionTimeToFirstToken.Record(
                    Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                    new KeyValuePair<string, object>(AITelemetry.ClientTag, ClientName));
            }

            ToolApprovalRequests.MarkCallsThatNeedNoApproval(updates.Current.Contents, chatOptions.Tools);

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
        return _clientFactory.CreateChatClientAsync(deployment, builder =>
        {
            builder.Use((innerClient, services) =>
            {
                var functionInvokingClient = new FunctionInvokingChatClient(innerClient, _loggerFactory, services)
                {
                    MaximumIterationsPerRequest = _defaultOptions.MaximumIterationsPerRequest,
                };

                return functionInvokingClient.UseTextToolResults();
            });

            // Innermost, as in NamedAICompletionClient, so every round trip and the tool calls between them are traced.
            if (_defaultOptions.EnableOpenTelemetry)
            {
                builder.UseOpenTelemetry(_loggerFactory, CrestAppsTelemetry.AIClientSourceName, telemetry => telemetry.EnableSensitiveData = _defaultOptions.EnableSensitiveData);
            }
        });
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
            Tools = [.. requestOptions.Functions, .. requestOptions.Declarations],
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
    /// Responses API reject the request. There are two exceptions, both in a run that resumes after a person
    /// answered tool approvals: the paused run's approval requests, which the function-invoking client turns into
    /// calls and results before anything reaches the service, and the tool calls the paused turn made before it
    /// paused, which are kept with their results so the model sees what the turn already did and does not do it
    /// again.
    /// </remarks>
    /// <param name="messages">The conversation messages.</param>
    /// <param name="context">The completion context.</param>
    private static List<ChatMessage> GetResponsesPrompts(IEnumerable<ChatMessage> messages, AICompletionContext context)
    {
        var source = messages.ToList();
        var resumedTurnStart = GetResumedTurnStart(source);
        var history = new List<ChatMessage>();

        for (var index = 0; index < source.Count; index++)
        {
            var message = source[index];

            if (message.Role == ChatRole.User && (!string.IsNullOrEmpty(message.Text) || message.Contents.Count > 0))
            {
                history.Add(message);
            }
            else if (message.Role == ChatRole.Assistant && message.Contents.Any(IsApprovalRequest))
            {
                history.Add(message);
            }
            else if (resumedTurnStart >= 0 && index >= resumedTurnStart && IsAnsweredToolCallMessage(source, index))
            {
                // The calls and, in the next message, their results.
                history.Add(message);
                history.Add(source[++index]);
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

        var kept = context.PastMessagesCount > 0
            ? history.TakeLast(context.PastMessagesCount.Value).ToList()
            : history;

        // A history cut to its newest messages can start with tool results whose tool calls were cut off, which the
        // service rejects.
        while (kept.Count > 0 && kept[0].Role == ChatRole.Tool)
        {
            kept.RemoveAt(0);
        }

        prompts.AddRange(kept);

        return prompts;
    }

#pragma warning disable MEAI001 // Tool approvals are a preview API of Microsoft.Extensions.AI.
    private static bool IsApprovalRequest(AIContent content)
    {
        return content is ToolApprovalRequestContent;
    }

    /// <summary>
    /// Gets where the turn that paused for tool approvals starts, when the messages end with a person's answers to
    /// them: after the last user message that has text.
    /// </summary>
    /// <param name="messages">The conversation messages.</param>
    /// <returns>The index of the first message of the paused turn, or <c>-1</c> when the request does not resume one.</returns>
    private static int GetResumedTurnStart(List<ChatMessage> messages)
    {
        if (messages.Count == 0 || !messages[^1].Contents.Any(content => content is ToolApprovalResponseContent))
        {
            return -1;
        }

        for (var index = messages.Count - 2; index >= 0; index--)
        {
            if (messages[index].Role == ChatRole.User && !string.IsNullOrEmpty(messages[index].Text))
            {
                return index + 1;
            }
        }

        return 0;
    }
#pragma warning restore MEAI001

    /// <summary>
    /// Determines whether a message holds only tool calls and the next message holds the result of each of them, so
    /// the pair can be sent to the service as it is.
    /// </summary>
    /// <param name="messages">The conversation messages.</param>
    /// <param name="index">The index of the message that may hold the tool calls.</param>
    /// <returns><see langword="true"/> when the message and the next one are a complete pair of calls and results.</returns>
    private static bool IsAnsweredToolCallMessage(List<ChatMessage> messages, int index)
    {
        var message = messages[index];

        if (message.Role != ChatRole.Assistant ||
            message.Contents.Count == 0 ||
            index + 1 >= messages.Count ||
            messages[index + 1].Role != ChatRole.Tool)
        {
            return false;
        }

        var resultIds = messages[index + 1].Contents
            .OfType<FunctionResultContent>()
            .Select(result => result.CallId)
            .ToHashSet(StringComparer.Ordinal);

        return message.Contents.All(content => content is FunctionCallContent call && resultIds.Contains(call.CallId)) &&
            resultIds.Count == message.Contents.Count &&
            messages[index + 1].Contents.All(content => content is FunctionResultContent);
    }
}
