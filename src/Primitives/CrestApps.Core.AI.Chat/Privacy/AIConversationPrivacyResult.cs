namespace CrestApps.Core.AI.Chat.Privacy;

/// <summary>
/// What one <see cref="AIConversationPrivacyService.ProcessAsync"/> call did for a profile.
/// </summary>
public sealed class AIConversationPrivacyResult
{
    /// <summary>
    /// Gets or sets where the next call starts: the last activity of the last conversation the storage mode was
    /// applied to, or <see langword="null"/> when the profile keeps everything and the host should forget the cursor.
    /// </summary>
    public DateTime? Cursor { get; set; }

    /// <summary>
    /// Gets or sets how many conversations the storage mode was applied to.
    /// </summary>
    public int Applied { get; set; }

    /// <summary>
    /// Gets or sets how many conversations past the profile's retention were deleted.
    /// </summary>
    public int Deleted { get; set; }
}
