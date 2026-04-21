namespace CrestApps.Core.Blazor.Web.ViewModels;

public sealed class UsageAnalyticsIndexViewModel
{
    public bool IsAIUsageTrackingEnabled { get; set; }

    public DateTime? StartDateUtc { get; set; }

    public DateTime? EndDateUtc { get; set; }

    public bool ShowReport { get; set; }

    public int TotalCalls { get; set; }

    public int TotalSessions { get; set; }

    public int TotalChatInteractions { get; set; }

    public long TotalTokens { get; set; }

    public IReadOnlyList<AICompletionUsageSummaryViewModel> Rows { get; set; } = [];
}

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
}
