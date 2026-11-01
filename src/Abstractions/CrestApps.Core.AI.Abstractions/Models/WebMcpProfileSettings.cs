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

    /// <summary>
    /// Gets or sets a value indicating whether the profile's chat may also call page tools that declare the WebMCP
    /// <c>consequentialHint</c> annotation, such as placing an order or sending a payment. The default is
    /// <see langword="false"/>, which leaves them out.
    /// </summary>
    public bool AllowConsequentialPageTools { get; set; }
}
