using System.Globalization;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;

namespace CrestApps.Core.Blazor.Web.ViewModels;

public static class UsageCostFormatter
{
    /// <summary>
    /// Formats an estimated cost, with four decimals below one so that small costs do not read as zero.
    /// </summary>
    public static string Format(string currency, decimal? cost)
    {
        if (cost is null || string.IsNullOrEmpty(currency))
        {
            return "-";
        }

        var format = cost.Value is > 0 and < 1 ? "N4" : "N2";

        return $"{currency} {cost.Value.ToString(format, CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Sums the estimated costs per currency.
    /// </summary>
    public static IReadOnlyList<string> Totals(IEnumerable<(string Currency, decimal? Cost)> costs)
        => costs
            .Where(entry => entry.Cost is not null && !string.IsNullOrEmpty(entry.Currency))
            .GroupBy(entry => entry.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => Format(group.Key, group.Sum(entry => entry.Cost.Value)))
            .ToList();

    /// <summary>
    /// Describes the usage that has no column of its own: cache writes, audio, image, and prediction tokens, web
    /// searches and fetches, audio length, characters, images, and any other count the provider reported.
    /// </summary>
    public static IReadOnlyList<string> DescribeOtherUsage(IReadOnlyCollection<AICompletionUsageRecord> records)
    {
        var details = new List<string>();

        void Add(string label, long value)
        {
            if (value > 0)
            {
                details.Add($"{label}: {value.ToString("N0", CultureInfo.InvariantCulture)}");
            }
        }

        Add("Cache writes", records.Sum(record => (long)record.CacheWriteInputTokenCount));
        Add("Audio in", records.Sum(record => (long)record.InputAudioTokenCount));
        Add("Cached audio in", records.Sum(record => (long)record.CachedInputAudioTokenCount));
        Add("Audio out", records.Sum(record => (long)record.OutputAudioTokenCount));
        Add("Image in", records.Sum(record => (long)record.InputImageTokenCount));
        Add("Image out", records.Sum(record => (long)record.OutputImageTokenCount));
        Add("Reasoning", records.Sum(record => (long)record.ReasoningTokenCount));
        Add("Accepted prediction", records.Sum(record => (long)record.AcceptedPredictionTokenCount));
        Add("Rejected prediction", records.Sum(record => (long)record.RejectedPredictionTokenCount));
        Add("Web searches", records.Sum(record => (long)record.WebSearchCount));
        Add("Web fetches", records.Sum(record => (long)record.WebFetchCount));
        Add("Audio seconds", records.Sum(record => record.AudioDurationMs) / 1_000);
        Add("Characters", records.Sum(record => (long)record.CharacterCount));
        Add("Images", records.Sum(record => (long)record.ImageCount));

        foreach (var (name, count) in AIUsageReport.SumAdditionalCounts(records).OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            Add(name, count);
        }

        return details;
    }
}
