using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Builds provider-agnostic AI usage reports from metered requests: one per model, or per model broken down by
/// category or purpose. Each row sums every billable unit, so it can be priced against the provider's meters.
/// </summary>
public static class AIUsageReport
{
    /// <summary>
    /// The label used for a model, category, or purpose that was not recorded.
    /// </summary>
    public const string Unspecified = "Unspecified";

    /// <summary>
    /// Builds the report rows, ordered by total tokens and then by request count, both descending.
    /// </summary>
    /// <param name="records">The metered requests.</param>
    /// <param name="grouping">How to group them.</param>
    public static IReadOnlyList<AIUsageReportRow> Build(IEnumerable<AICompletionUsageRecord> records, AIUsageReportGrouping grouping)
    {
        ArgumentNullException.ThrowIfNull(records);

        return records
            .GroupBy(record => new GroupKey(
                record.ClientName ?? Unspecified,
                GetModelName(record),
                GetOperationType(record),
                grouping == AIUsageReportGrouping.ModelAndCategory ? Label(record.ContextType) : null,
                grouping == AIUsageReportGrouping.ModelAndPurpose ? Label(record.Purpose) : null))
            .Select(group => Summarize(group.Key, group.ToList()))
            .OrderByDescending(row => row.TotalTokenCount)
            .ThenByDescending(row => row.RequestCount)
            .ThenBy(row => row.ModelName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.OperationType, StringComparer.Ordinal)
            .ThenBy(row => row.ContextType ?? row.Purpose, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets the kind of request a record metered. Records written before kinds were recorded were all chat
    /// completions.
    /// </summary>
    /// <param name="record">The record.</param>
    public static string GetOperationType(AICompletionUsageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return string.IsNullOrEmpty(record.OperationType)
            ? AIUsageOperationTypes.Chat
            : record.OperationType;
    }

    /// <summary>
    /// Gets the model a record metered: the model the provider reported, or the deployment the request was sent to.
    /// </summary>
    /// <param name="record">The record.</param>
    public static string GetModelName(AICompletionUsageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return Label(string.IsNullOrEmpty(record.ModelName) ? record.DeploymentName : record.ModelName);
    }

    private static string Label(string value) => string.IsNullOrEmpty(value) ? Unspecified : value;

    private static AIUsageReportRow Summarize(GroupKey key, List<AICompletionUsageRecord> records)
    {
        var latencySamples = records.Where(record => record.ResponseLatencyMs > 0).ToList();

        return new AIUsageReportRow
        {
            ClientName = key.ClientName,
            ModelName = key.ModelName,
            OperationType = key.OperationType,
            ContextType = key.ContextType,
            Purpose = key.Purpose,
            RequestCount = records.Count,
            InputTokenCount = records.Sum(record => (long)record.InputTokenCount),
            CachedInputTokenCount = records.Sum(record => (long)record.CachedInputTokenCount),
            OutputTokenCount = records.Sum(record => (long)record.OutputTokenCount),
            ReasoningTokenCount = records.Sum(record => (long)record.ReasoningTokenCount),
            InputAudioTokenCount = records.Sum(record => (long)record.InputAudioTokenCount),
            OutputAudioTokenCount = records.Sum(record => (long)record.OutputAudioTokenCount),
            TotalTokenCount = records.Sum(record => (long)record.TotalTokenCount),
            AudioDurationMs = records.Sum(record => record.AudioDurationMs),
            CharacterCount = records.Sum(record => (long)record.CharacterCount),
            ImageCount = records.Sum(record => (long)record.ImageCount),
            AverageResponseLatencyMs = latencySamples.Count > 0
                ? Math.Round(latencySamples.Average(record => record.ResponseLatencyMs), 0)
                : 0,
        };
    }

    private sealed record GroupKey(
        string ClientName,
        string ModelName,
        string OperationType,
        string ContextType,
        string Purpose);
}
