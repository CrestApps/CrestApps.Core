using System.Diagnostics;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Emits every metered AI request as the <c>crestapps.core.ai.usage.*</c> metrics, so request counts, tokens and
/// latency can be charted for every client kind, including embeddings, speech and realtime, which the
/// <c>gen_ai.*</c> chat metrics do not cover.
/// </summary>
/// <remarks>
/// The tags are bounded: operation, client, deployment, purpose and whether the response was streamed. Session,
/// user and profile ids stay in the stored usage records and never reach a metric.
/// </remarks>
internal sealed class AIUsageTelemetryObserver : IAICompletionUsageObserver
{
    /// <summary>
    /// Records the metrics of one metered request.
    /// </summary>
    /// <param name="record">The usage record describing the completed request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task UsageRecordedAsync(AICompletionUsageRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var tags = new TagList
        {
            { CrestAppsTelemetry.OperationTag, record.OperationType },
            { AITelemetry.ClientTag, record.ClientName },
            { AITelemetry.DeploymentTag, record.DeploymentName },
            { AITelemetry.PurposeTag, record.Purpose },
            { AITelemetry.StreamingTag, record.IsStreaming },
        };

        AITelemetry.UsageRequests.Add(1, tags);
        AITelemetry.UsageDuration.Record(record.ResponseLatencyMs / 1000, tags);

        AddTokens(record.InputTokenCount, "input", tags);
        AddTokens(record.OutputTokenCount, "output", tags);
        AddTokens(record.CachedInputTokenCount, "cached", tags);
        AddTokens(record.ReasoningTokenCount, "reasoning", tags);
        AddTokens(record.InputAudioTokenCount + record.OutputAudioTokenCount, "audio", tags);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Counts the tokens of one kind, when the request used any.
    /// </summary>
    /// <param name="count">The number of tokens.</param>
    /// <param name="tokenType">The kind of tokens.</param>
    /// <param name="tags">The request's tags; this copy gets the token type added.</param>
    private static void AddTokens(int count, string tokenType, TagList tags)
    {
        if (count <= 0)
        {
            return;
        }

        tags.Add(AITelemetry.TokenTypeTag, tokenType);

        AITelemetry.UsageTokens.Add(count, tags);
    }
}
