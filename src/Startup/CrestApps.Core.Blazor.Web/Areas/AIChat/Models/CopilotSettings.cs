using CrestApps.Core.AI.Copilot.Models;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Models;

public sealed class CopilotSettings
{
    public CopilotAuthenticationType AuthenticationType { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ProtectedClientSecret { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = ["user:email", "read:org"];
    public string ProviderType { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ProtectedApiKey { get; set; } = string.Empty;
    public string WireApi { get; set; } = "completions";
    public string DefaultModel { get; set; } = string.Empty;
    public string AzureApiVersion { get; set; } = string.Empty;
}
