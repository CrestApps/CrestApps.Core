namespace CrestApps.Core.AI.Chat.Privacy;

/// <summary>
/// Options of <see cref="AIConversationPrivacyService"/>.
/// </summary>
public sealed class AIConversationPrivacyOptions
{
    /// <summary>
    /// Gets or sets how long a conversation is left alone after its last activity before its storage mode is
    /// applied, so it has been closed and its post-session processing has read the transcript first. Defaults to
    /// 15 minutes.
    /// </summary>
    public TimeSpan SettleTime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets or sets the largest number of conversations of one profile a single
    /// <see cref="AIConversationPrivacyService.ProcessAsync"/> call redacts, strips or deletes, each. Defaults to 50.
    /// </summary>
    public int BatchSize { get; set; } = 50;
}
