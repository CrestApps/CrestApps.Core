namespace CrestApps.Core.Mvc.Web.Areas.AIChat.ViewModels;

public sealed class AICompletionUsageSummaryViewModel
{
    public string UserLabel { get; set; }

    public bool IsAuthenticated { get; set; }

    public string ClientName { get; set; }

    public string ModelName { get; set; }

    public int TotalCalls { get; set; }

    public int TotalSessions { get; set; }

    public int TotalChatInteractions { get; set; }

    public long TotalInputTokens { get; set; }

    public long TotalOutputTokens { get; set; }

    public long TotalTokens { get; set; }

    public double AverageResponseLatencyMs { get; set; }

    public long TotalCachedInputTokens { get; set; }

    public IReadOnlyList<string> OtherUsage { get; set; } = [];

    public string Currency { get; set; }

    public decimal? EstimatedCost { get; set; }
}
