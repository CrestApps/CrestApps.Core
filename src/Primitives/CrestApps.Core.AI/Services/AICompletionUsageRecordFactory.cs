using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Provides functionality for AI Completion Usage Record Factory.
/// </summary>
public static class AICompletionUsageRecordFactory
{
    /// <summary>
    /// Creates the operation.
    /// </summary>
    /// <param name="completionContext">The completion context.</param>
    /// <param name="clientName">The client name.</param>
    /// <param name="connectionName">The connection name.</param>
    /// <param name="deploymentName">The deployment name.</param>
    /// <param name="modelName">The model name.</param>
    /// <param name="responseId">The response id.</param>
    /// <param name="inputTokenCount">The input token count.</param>
    /// <param name="outputTokenCount">The output token count.</param>
    /// <param name="totalTokenCount">The total token count.</param>
    /// <param name="responseLatencyMs">The response latency ms.</param>
    /// <param name="isStreaming">Indicates whether streaming.</param>
    public static AICompletionUsageRecord Create(AICompletionContext completionContext, string clientName, string connectionName, string deploymentName, string modelName, string responseId, long inputTokenCount, long outputTokenCount, long totalTokenCount, double responseLatencyMs, bool isStreaming)
    {
        return Create(completionContext?.AdditionalProperties, clientName, connectionName, deploymentName, modelName, responseId, inputTokenCount, outputTokenCount, totalTokenCount, responseLatencyMs, isStreaming);
    }

    /// <summary>
    /// Creates the operation.
    /// </summary>
    /// <param name="additionalProperties">The additional properties.</param>
    /// <param name="clientName">The client name.</param>
    /// <param name="connectionName">The connection name.</param>
    /// <param name="deploymentName">The deployment name.</param>
    /// <param name="modelName">The model name.</param>
    /// <param name="responseId">The response id.</param>
    /// <param name="inputTokenCount">The input token count.</param>
    /// <param name="outputTokenCount">The output token count.</param>
    /// <param name="totalTokenCount">The total token count.</param>
    /// <param name="responseLatencyMs">The response latency ms.</param>
    /// <param name="isStreaming">The is streaming.</param>
    public static AICompletionUsageRecord Create(IReadOnlyDictionary<string, object> additionalProperties, string clientName, string connectionName, string deploymentName, string modelName, string responseId, long inputTokenCount, long outputTokenCount, long totalTokenCount, double responseLatencyMs, bool isStreaming)
    {
        var usage = new UsageDetails
        {
            InputTokenCount = inputTokenCount,
            OutputTokenCount = outputTokenCount,
            TotalTokenCount = totalTokenCount,
        };

        return Create(additionalProperties, AIUsageOperationTypes.Chat, clientName, connectionName, deploymentName, modelName, responseId, usage, responseLatencyMs, isStreaming);
    }

    /// <summary>
    /// Creates a usage record for any kind of AI request from the provider-agnostic usage the response reported.
    /// The category and purpose are resolved from the request's additional properties first, then from the
    /// current <see cref="AIUsageScope"/>, and finally from the chat session or interaction the request belongs to.
    /// </summary>
    /// <param name="additionalProperties">The request's additional properties, or <see langword="null"/>.</param>
    /// <param name="operationType">The kind of request, one of <see cref="AIUsageOperationTypes"/>.</param>
    /// <param name="clientName">The client (provider) name.</param>
    /// <param name="connectionName">The connection name.</param>
    /// <param name="deploymentName">The deployment name.</param>
    /// <param name="modelName">The model name.</param>
    /// <param name="responseId">The response id.</param>
    /// <param name="usage">The usage the response reported, or <see langword="null"/> when it reported none.</param>
    /// <param name="responseLatencyMs">The response latency in milliseconds.</param>
    /// <param name="isStreaming">Whether the response was streamed.</param>
    public static AICompletionUsageRecord Create(IReadOnlyDictionary<string, object> additionalProperties, string operationType, string clientName, string connectionName, string deploymentName, string modelName, string responseId, UsageDetails usage, double responseLatencyMs, bool isStreaming)
    {
        var record = new AICompletionUsageRecord
        {
            OperationType = operationType,
            ClientName = clientName,
            ConnectionName = connectionName,
            DeploymentName = deploymentName,
            ModelName = modelName,
            ResponseId = responseId,
            ResponseLatencyMs = responseLatencyMs,
            IsStreaming = isStreaming,
        };

        ApplyUsage(record, usage);

        if (additionalProperties?.TryGetValue(AICompletionContextKeys.Session, out var sessionValue) == true && sessionValue is AIChatSession session)
        {
            record.ContextType = nameof(AIChatSession);
            record.SessionId = session.SessionId;
            record.ProfileId = session.ProfileId;
            record.UserId = session.UserId;
            record.ClientId = session.ClientId;
            record.IsAuthenticated = !string.IsNullOrEmpty(session.UserId);
            record.VisitorId = record.IsAuthenticated ? session.UserId : session.ClientId;
        }

        if (additionalProperties?.TryGetValue(AICompletionContextKeys.Interaction, out var interactionValue) == true && interactionValue is ChatInteraction interaction)
        {
            record.ContextType = nameof(ChatInteraction);
            record.InteractionId = interaction.ItemId;
            record.UserId = interaction.OwnerId;
            record.UserName = interaction.Author;
            record.IsAuthenticated = !string.IsNullOrEmpty(interaction.OwnerId);
            record.VisitorId = interaction.OwnerId;
        }
        else if (additionalProperties?.TryGetValue(AICompletionContextKeys.InteractionId, out var interactionIdValue) == true && interactionIdValue is string interactionId && !string.IsNullOrEmpty(interactionId))
        {
            record.ContextType ??= nameof(ChatInteraction);
            record.InteractionId = interactionId;
        }

        ApplyLabels(record, additionalProperties);

        return record;
    }

    /// <summary>
    /// Copies the provider-agnostic usage a response reported onto a record. Counts the provider did not report
    /// are left at zero.
    /// </summary>
    /// <param name="record">The record to update.</param>
    /// <param name="usage">The reported usage, or <see langword="null"/> when the response reported none.</param>
    public static void ApplyUsage(AICompletionUsageRecord record, UsageDetails usage)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (usage is null)
        {
            return;
        }

        var inputTokenCount = usage.InputTokenCount ?? 0;
        var outputTokenCount = usage.OutputTokenCount ?? 0;

        record.InputTokenCount = Normalize(inputTokenCount);
        record.OutputTokenCount = Normalize(outputTokenCount);
        record.TotalTokenCount = Normalize(usage.TotalTokenCount is > 0
            ? usage.TotalTokenCount.Value
            : inputTokenCount + outputTokenCount);
        record.CachedInputTokenCount = Normalize(usage.CachedInputTokenCount ?? 0);
        record.ReasoningTokenCount = Normalize(usage.ReasoningTokenCount ?? 0);
#pragma warning disable MEAI001 // The audio token counts are for evaluation purposes only and may change in future updates.
        record.InputAudioTokenCount = Normalize(usage.InputAudioTokenCount ?? 0);
        record.OutputAudioTokenCount = Normalize(usage.OutputAudioTokenCount ?? 0);
#pragma warning restore MEAI001 // The audio token counts are for evaluation purposes only and may change in future updates.

        if (usage.AdditionalCounts is not { Count: > 0 } additionalCounts)
        {
            return;
        }

        if (additionalCounts.TryGetValue(AIUsageAdditionalCounts.AudioDurationMs, out var audioDurationMs))
        {
            record.AudioDurationMs = Math.Max(0, audioDurationMs);
        }

        if (additionalCounts.TryGetValue(AIUsageAdditionalCounts.CharacterCount, out var characterCount))
        {
            record.CharacterCount = Normalize(characterCount);
        }

        if (additionalCounts.TryGetValue(AIUsageAdditionalCounts.ImageCount, out var imageCount))
        {
            record.ImageCount = Normalize(imageCount);
        }
    }

    /// <summary>
    /// Applies the usage category and purpose: a value set on the request takes precedence over the current
    /// <see cref="AIUsageScope"/>, which takes precedence over a default purpose set on the request
    /// (<see cref="AICompletionContextKeys.DefaultUsagePurpose"/>) and over the category already derived from the
    /// request's chat session or interaction.
    /// </summary>
    /// <param name="record">The record to update.</param>
    /// <param name="additionalProperties">The request's additional properties, or <see langword="null"/>.</param>
    public static void ApplyLabels(AICompletionUsageRecord record, IReadOnlyDictionary<string, object> additionalProperties)
    {
        ArgumentNullException.ThrowIfNull(record);

        var scope = AIUsageScope.Current;

        record.ContextType = GetString(additionalProperties, AICompletionContextKeys.UsageContextType)
            ?? scope?.ContextType
            ?? record.ContextType;
        record.Purpose = GetString(additionalProperties, AICompletionContextKeys.UsagePurpose)
            ?? scope?.Purpose
            ?? GetString(additionalProperties, AICompletionContextKeys.DefaultUsagePurpose)
            ?? record.Purpose;
    }

    private static string GetString(IReadOnlyDictionary<string, object> additionalProperties, string key)
    {
        if (additionalProperties?.TryGetValue(key, out var value) == true &&
            value is string text &&
            !string.IsNullOrEmpty(text))
        {
            return text;
        }

        return null;
    }

    private static int Normalize(long value) => value > int.MaxValue ? int.MaxValue : (int)Math.Max(0, value);
}
