namespace CrestApps.Core.AI.Models;

/// <summary>
/// Marks a chat session the profile's storage mode was applied to, so it is applied once.
/// </summary>
public sealed class AIConversationPrivacyMetadata
{
    /// <summary>
    /// Gets or sets the mode applied.
    /// </summary>
    public AIDataStorageMode Mode { get; set; }

    /// <summary>
    /// Gets or sets when it was applied.
    /// </summary>
    public DateTime AppliedUtc { get; set; }
}
