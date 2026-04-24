using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI;

public sealed class AIDeploymentProviderEntry
{
    public LocalizedString DisplayName { get; set; }

    public LocalizedString Description { get; set; }

    /// <summary>
    /// When <c>true</c>, deployments under this provider carry their own connection
    /// parameters (endpoint, credentials) in <see cref="AIDeployment.Properties"/>
    /// instead of referencing a shared <c>AIProviderConnection</c>.
    /// </summary>
    public bool UseContainedConnection { get; set; }
}
