using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI;

/// <summary>
/// Represents the AI Deployment Provider Entry.
/// </summary>
public sealed class AIDeploymentProviderEntry
{
    /// <summary>
    /// Gets or sets the display Name.
    /// </summary>
    public LocalizedString DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public LocalizedString Description { get; set; }

    /// <summary>
    /// When <c>true</c>, deployments under this provider carry their own connection
    /// parameters (endpoint, credentials) in <see cref="AIDeployment.Properties"/>
    /// instead of referencing a shared <c>AIProviderConnection</c>.
    /// </summary>
    public bool UseContainedConnection { get; set; }
}
