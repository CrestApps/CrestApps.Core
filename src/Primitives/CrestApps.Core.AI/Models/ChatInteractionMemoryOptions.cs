namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents the chat Interaction Memory Options.
/// </summary>
public sealed class ChatInteractionMemoryOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether enables User Memory.
    /// </summary>
    public bool EnableUserMemory { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether chat interactions remember the earlier parts of a long
    /// conversation. See <see cref="MemoryMetadata.EnableSessionMemory"/>.
    /// </summary>
    public bool EnableSessionMemory { get; set; } = true;
}
