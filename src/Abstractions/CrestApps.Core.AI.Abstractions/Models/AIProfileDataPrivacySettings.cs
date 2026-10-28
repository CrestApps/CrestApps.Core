namespace CrestApps.Core.AI.Models;

/// <summary>
/// How long an AI profile's conversations are kept and what is kept of them.
/// </summary>
public sealed class AIProfileDataPrivacySettings
{
    /// <summary>
    /// Gets or sets what is kept of a conversation once it ends.
    /// </summary>
    public AIDataStorageMode StorageMode { get; set; }

    /// <summary>
    /// Gets or sets when the storage mode was last changed. Conversations whose last activity is later get it.
    /// </summary>
    public DateTime? StorageModeSinceUtc { get; set; }

    /// <summary>
    /// Gets or sets after how many days without activity a conversation is deleted, or <see langword="null"/> to
    /// keep conversations.
    /// </summary>
    public int? RetentionDays { get; set; }
}
