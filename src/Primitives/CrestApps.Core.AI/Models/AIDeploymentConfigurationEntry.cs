using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents a deployment entry read from the application configuration (e.g., appsettings.json).
/// Used to define AI deployments for both connection-based and contained-connection providers.
/// </summary>
public sealed class AIDeploymentConfigurationEntry
{
    /// <summary>
    /// Gets or sets the deployment provider name for configuration entries.
    /// </summary>
    public string ClientName { get; set; }

    /// <summary>
    /// Gets or sets the unique technical deployment name used for lookups.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the provider-facing model or deployment name.
    /// Falls back to <see cref="Name"/> when not provided.
    /// </summary>
    public string ModelName { get; set; }

    /// <summary>
    /// Gets or sets the shared provider connection name for connection-based deployments.
    /// Leave empty for contained-connection deployments.
    /// </summary>
    public string ConnectionName { get; set; }

    /// <summary>
    /// Gets or sets the deployment capabilities (Chat, Utility, Embedding, Image, SpeechToText, TextToSpeech, Vision).
    /// </summary>
    public AIDeploymentCapability Capability { get; set; }

    /// <summary>
    /// Gets or sets the legacy deployment type flags.
    /// Use <see cref="Capability"/> for new code.
    /// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
    [Obsolete("Use Capability instead. Retained for backward compatibility.")]
    [JsonIgnore]
    public AIDeploymentType Type
    {
        get => Capability.ToLegacyType();
        set => Capability = value.ToCapability();
    }

    [JsonInclude]
    [JsonPropertyName("Type")]
    private AIDeploymentType LegacyType
    {
        set => Capability = value.ToCapability();
    }
#pragma warning restore CS0618 // Type or member is obsolete

    /// <summary>
    /// Gets or sets provider-specific properties for contained-connection deployments.
    /// These are usually flattened from top-level fields such as Endpoint, AuthenticationType, ApiKey, and IdentityId.
    /// </summary>
    public JsonObject Properties { get; set; }
}
