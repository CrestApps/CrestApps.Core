namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Models;

public sealed class ClaudeSettings
{
    public ClaudeAuthenticationType AuthenticationType { get; set; }

    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    public string? ProtectedApiKey { get; set; }

    public string? DefaultModel { get; set; }
}
