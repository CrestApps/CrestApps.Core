using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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
    /// Gets or sets the legacy purpose names this configuration entry declares, if any.
    /// </summary>
    /// <remarks>
    /// A deployment declares capabilities, not a purpose. Configuration written before that change still
    /// names one or more legacy purposes under <c>Purpose</c>, <c>Capability</c>, or <c>Type</c>, and
    /// <see cref="AIDeploymentPurposeCompatibility"/> projects those names onto capabilities when the
    /// deployment is materialized.
    /// </remarks>
    public string[] LegacyPurposes { get; set; }

    /// <summary>
    /// Gets or sets provider-specific properties for contained-connection deployments.
    /// These are usually flattened from top-level fields such as Endpoint, AuthenticationType, ApiKey, and IdentityId.
    /// </summary>
    public JsonObject Properties { get; set; }
}
