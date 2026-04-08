namespace CrestApps.Core.AI.Models;
public sealed class ChatInteractionMemoryOptions
{
    public bool EnableUserMemory { get; set; } = true;

    public ChatInteractionMemoryOptions Clone()
    {
        return new()
        {
            EnableUserMemory = EnableUserMemory,
        };
    }

    public static ChatInteractionMemoryOptions FromMetadata(MemoryMetadata metadata)
    {
        return new()
        {
            EnableUserMemory = metadata?.EnableUserMemory ?? true,
        };
    }
}