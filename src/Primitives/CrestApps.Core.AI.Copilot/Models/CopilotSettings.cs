namespace CrestApps.Core.AI.Copilot.Models;

/// <summary>
/// Represents the copilot Settings.
/// </summary>
public sealed class CopilotSettings
{
    /// <summary>
    /// Gets or sets the authentication Type.
    /// </summary>
    public CopilotAuthenticationType AuthenticationType { get; set; }

    /// <summary>
    /// Gets or sets the client ID.
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the protected Client Secret.
    /// </summary>
    public string ProtectedClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the scopes.
    /// </summary>
    public string[] Scopes { get; set; } = ["user:email", "read:org"];

    /// <summary>
    /// Gets or sets the provider Type.
    /// </summary>
    public string ProviderType { get; set; }

    /// <summary>
    /// Gets or sets the base Url.
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the protected Api Key.
    /// </summary>
    public string ProtectedApiKey { get; set; }

    /// <summary>
    /// Gets or sets the wire Api.
    /// </summary>
    public string WireApi { get; set; } = "completions";

    /// <summary>
    /// Gets or sets the default Model.
    /// </summary>
    public string DefaultModel { get; set; }

    /// <summary>
    /// Gets or sets the azure Api Version.
    /// </summary>
    public string AzureApiVersion { get; set; }
}
