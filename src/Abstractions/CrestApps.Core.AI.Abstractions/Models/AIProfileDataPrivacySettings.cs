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

    /// <summary>
    /// Gets or sets the names that are not personal data and are kept when personal data is redacted: the name the
    /// assistant gives itself (the persona's), the business's and its staff's. The customer's name is still redacted
    /// wherever either side says it.
    /// </summary>
    public List<string> NamesToKeep { get; set; } = [];
}
