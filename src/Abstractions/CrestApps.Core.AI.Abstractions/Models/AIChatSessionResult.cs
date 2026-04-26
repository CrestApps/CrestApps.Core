namespace CrestApps.Core.AI.Models;

public sealed class AIChatSessionResult
{
    public int Count { get; set; }

    public IEnumerable<AIChatSessionEntry> Sessions { get; set; }
}
