using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Mvc.Web.Areas.AIChat.ViewModels;

public sealed class AudioUsageAnalyticsIndexViewModel
{
    public DateTime? StartDateUtc { get; set; }

    public DateTime? EndDateUtc { get; set; }

    public AIAudioUsageReportGrouping Grouping { get; set; }

    public bool ShowReport { get; set; }

    public IReadOnlyList<AIAudioUsageReportRow> Rows { get; set; } = [];

    public int TotalRequests => Rows.Sum(row => row.RequestCount);

    public double TotalSpeechToTextHours => Rows.Where(row => row.OperationType == AIUsageOperationTypes.SpeechToText).Sum(row => row.AudioHours);

    public double TotalTextToSpeechHours => Rows.Where(row => row.OperationType == AIUsageOperationTypes.TextToSpeech).Sum(row => row.AudioHours);

    public long TotalCharacters => Rows.Sum(row => row.CharacterCount);
}
