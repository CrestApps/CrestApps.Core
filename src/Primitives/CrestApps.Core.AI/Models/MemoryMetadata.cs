namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents the memory Metadata.
/// </summary>
public sealed class MemoryMetadata
{
    /// <summary>
    /// Gets or sets a value indicating whether enables User Memory.
    /// </summary>
    public bool? EnableUserMemory { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the assistant remembers the earlier parts of a long conversation:
    /// the messages that fall out of the past-messages window are summarized, and older turns that bear on the
    /// current question are recalled. Works for anonymous visitors too, since it never leaves the conversation.
    /// </summary>
    public bool? EnableSessionMemory { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the assistant can look up the signed-in user's past
    /// conversations with the same profile. Never reaches another user's conversations or another profile's.
    /// </summary>
    public bool? EnablePastConversations { get; set; }
}
