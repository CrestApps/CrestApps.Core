namespace CrestApps.Core.AI.Chat.AGUI;

/// <summary>
/// The options of the AI chat's AG-UI runs.
/// </summary>
public sealed class AGUIChatOptions
{
    /// <summary>
    /// Gets or sets how long a run keeps going after its connection dropped, for example because a page tool opened
    /// another page, so its answer is still saved. Two minutes by default; <see cref="TimeSpan.Zero"/> ends the run
    /// with the connection.
    /// </summary>
    public TimeSpan DisconnectedRunTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
