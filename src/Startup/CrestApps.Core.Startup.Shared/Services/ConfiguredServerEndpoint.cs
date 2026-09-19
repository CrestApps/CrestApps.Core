namespace CrestApps.Core.Startup.Shared.Services;

/// <summary>
/// Describes a single configured sample-host server target.
/// </summary>
public sealed class ConfiguredServerEndpoint
{
    /// <summary>
    /// Gets or sets the configuration key for the server target.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the friendly label shown in the sample client UI.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base endpoint used by the client sample.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional API key sent to the target server.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
