using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Builds audio usage reports from metered speech-to-text and text-to-speech requests: total audio hours, request
/// counts, characters, audio size and tokens, per model and optionally per voice or language. Requests of other
/// kinds are ignored; <see cref="AIUsageReport"/> covers them.
/// </summary>
public static class AIAudioUsageReport
{
    /// <summary>
    /// Builds the report rows, ordered by audio length and then by request count, both descending.
    /// </summary>
    /// <param name="records">The metered requests.</param>
    /// <param name="grouping">How to group them.</param>
    public static IReadOnlyList<AIAudioUsageReportRow> Build(IEnumerable<AICompletionUsageRecord> records, AIAudioUsageReportGrouping grouping)
    {
        ArgumentNullException.ThrowIfNull(records);

        return records
            .Where(IsAudioRecord)
            .GroupBy(record => new GroupKey(
                record.ClientName ?? AIUsageReport.Unspecified,
                AIUsageReport.GetModelName(record),
                AIUsageReport.GetOperationType(record),
                grouping == AIAudioUsageReportGrouping.ModelAndVoice ? Label(record.VoiceId) : null,
                grouping == AIAudioUsageReportGrouping.ModelAndLanguage ? Label(record.Language) : null))
            .Select(group => Summarize(group.Key, group.ToList()))
            .OrderByDescending(row => row.AudioDurationMs)
            .ThenByDescending(row => row.RequestCount)
            .ThenBy(row => row.ModelName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.OperationType, StringComparer.Ordinal)
            .ThenBy(row => row.VoiceId ?? row.Language, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets a value indicating whether a record metered a speech-to-text or text-to-speech request.
    /// </summary>
    /// <param name="record">The record.</param>
    public static bool IsAudioRecord(AICompletionUsageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.OperationType is AIUsageOperationTypes.SpeechToText or AIUsageOperationTypes.TextToSpeech;
    }

    private static string Label(string value) => string.IsNullOrEmpty(value) ? AIUsageReport.Unspecified : value;

    private static AIAudioUsageReportRow Summarize(GroupKey key, List<AICompletionUsageRecord> records)
    {
        var latencySamples = records.Where(record => record.ResponseLatencyMs > 0).ToList();

        return new AIAudioUsageReportRow
        {
            ClientName = key.ClientName,
            ModelName = key.ModelName,
            OperationType = key.OperationType,
            VoiceId = key.VoiceId,
            Language = key.Language,
            RequestCount = records.Count,
            AudioDurationMs = records.Sum(record => record.AudioDurationMs),
            RequestsWithDurationCount = records.Count(record => record.AudioDurationMs > 0),
            SpeechDurationMs = records.Sum(record => Math.Max(0, record.AudioDurationMs - record.AudioStartMs)),
            CharacterCount = records.Sum(record => (long)record.CharacterCount),
            AudioInputBytes = records.Sum(record => record.AudioInputBytes),
            AudioOutputBytes = records.Sum(record => record.AudioOutputBytes),
            SegmentCount = records.Sum(record => (long)record.SegmentCount),
            InputTokenCount = records.Sum(record => (long)record.InputTokenCount),
            OutputTokenCount = records.Sum(record => (long)record.OutputTokenCount),
            TotalTokenCount = records.Sum(record => (long)record.TotalTokenCount),
            AverageResponseLatencyMs = latencySamples.Count > 0
                ? Math.Round(latencySamples.Average(record => record.ResponseLatencyMs), 0)
                : 0,
        };
    }

    private sealed record GroupKey(
        string ClientName,
        string ModelName,
        string OperationType,
        string VoiceId,
        string Language);
}
