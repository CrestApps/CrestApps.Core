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
        => Build(records, grouping, pricingByDeployment: null);

    /// <summary>
    /// Builds the report rows with the estimated cost of each, ordered by total tokens and then by request count,
    /// both descending. Requests to deployments priced in different currencies, or not priced at all, are kept in
    /// separate rows.
    /// </summary>
    /// <param name="records">The metered requests.</param>
    /// <param name="grouping">How to group them.</param>
    /// <param name="pricingByDeployment">The pricing of each deployment by name, from
    /// <see cref="AIUsageCostEstimator.GetPricingByDeploymentAsync"/>.</param>
    public static IReadOnlyList<AIUsageReportRow> Build(
        IEnumerable<AICompletionUsageRecord> records,
        AIUsageReportGrouping grouping,
        IReadOnlyDictionary<string, AIDeploymentPricing> pricingByDeployment)
    {
        ArgumentNullException.ThrowIfNull(records);

        return records
            .GroupBy(record => new GroupKey(
                record.ClientName ?? Unspecified,
                GetModelName(record),
                GetOperationType(record),
                grouping == AIUsageReportGrouping.ModelAndCategory ? Label(record.ContextType) : null,
                grouping == AIUsageReportGrouping.ModelAndPurpose ? Label(record.Purpose) : null,
                AIUsageCostEstimator.GetPricing(pricingByDeployment, record)?.Currency))
            .Select(group => Summarize(group.Key, group.ToList(), pricingByDeployment))
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

    private static AIUsageReportRow Summarize(GroupKey key, List<AICompletionUsageRecord> records, IReadOnlyDictionary<string, AIDeploymentPricing> pricingByDeployment)
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
            CacheWriteInputTokenCount = records.Sum(record => (long)record.CacheWriteInputTokenCount),
            CachedInputAudioTokenCount = records.Sum(record => (long)record.CachedInputAudioTokenCount),
            InputImageTokenCount = records.Sum(record => (long)record.InputImageTokenCount),
            OutputImageTokenCount = records.Sum(record => (long)record.OutputImageTokenCount),
            AcceptedPredictionTokenCount = records.Sum(record => (long)record.AcceptedPredictionTokenCount),
            RejectedPredictionTokenCount = records.Sum(record => (long)record.RejectedPredictionTokenCount),
            TotalTokenCount = records.Sum(record => (long)record.TotalTokenCount),
            WebSearchCount = records.Sum(record => (long)record.WebSearchCount),
            WebFetchCount = records.Sum(record => (long)record.WebFetchCount),
            AdditionalCounts = SumAdditionalCounts(records),
            AudioDurationMs = records.Sum(record => record.AudioDurationMs),
            CharacterCount = records.Sum(record => (long)record.CharacterCount),
            ImageCount = records.Sum(record => (long)record.ImageCount),
            AverageResponseLatencyMs = latencySamples.Count > 0
                ? Math.Round(latencySamples.Average(record => record.ResponseLatencyMs), 0)
                : 0,
            Currency = key.Currency,
            EstimatedCost = AIUsageCostEstimator.Estimate(records, pricingByDeployment),
        };
    }

    /// <summary>
    /// Sums the counts providers reported without a field of their own, by name.
    /// </summary>
    /// <param name="records">The records.</param>
    public static Dictionary<string, long> SumAdditionalCounts(IEnumerable<AICompletionUsageRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var totals = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            if (record.AdditionalCounts is null)
            {
                continue;
            }

            foreach (var (name, count) in record.AdditionalCounts)
            {
                totals[name] = totals.GetValueOrDefault(name) + count;
            }
        }

        return totals;
    }

    private sealed record GroupKey(
        string ClientName,
        string ModelName,
        string OperationType,
        string ContextType,
        string Purpose,
        string Currency);
}
