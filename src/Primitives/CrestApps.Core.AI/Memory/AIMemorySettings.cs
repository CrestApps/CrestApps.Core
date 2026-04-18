namespace CrestApps.Core.AI.Models;

public sealed class AIMemorySettings
{
    public string IndexProfileName { get; set; }

    public int TopN { get; set; } = 5;
}
