namespace CrestApps.Core.AI.Models;

/// <summary>
/// Settings stored on <see cref="AIProfile.Settings"/> that control whether the profile's chat may use the WebMCP
/// page tools of the browser tab the user chats from.
/// </summary>
public sealed class WebMcpProfileSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the profile's chat may call the page tools the chat's browser tab
    /// registered. The default is <see langword="false"/>.
    /// </summary>
    public bool AllowPageTools { get; set; }
}
