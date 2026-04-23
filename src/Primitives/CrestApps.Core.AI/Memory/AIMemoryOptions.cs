namespace CrestApps.Core.AI.Memory;

public sealed class AIMemoryOptions
{
    public string IndexProfileName { get; set; }

    public int TopN { get; set; } = 5;
}
