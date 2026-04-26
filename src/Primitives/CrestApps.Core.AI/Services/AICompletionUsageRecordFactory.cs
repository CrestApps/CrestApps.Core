using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;

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
        var record = new AICompletionUsageRecord
        {
            ClientName = clientName,
            ConnectionName = connectionName,
            DeploymentName = deploymentName,
            ModelName = modelName,
            ResponseId = responseId,
            InputTokenCount = Normalize(inputTokenCount),
            OutputTokenCount = Normalize(outputTokenCount),
            TotalTokenCount = Normalize(totalTokenCount > 0 ? totalTokenCount : inputTokenCount + outputTokenCount),
            ResponseLatencyMs = responseLatencyMs,
            IsStreaming = isStreaming,
        };
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

        return record;
    }

    private static int Normalize(long value) => value > int.MaxValue ? int.MaxValue : (int)value;
}
