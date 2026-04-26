namespace CrestApps.Core.AI.Models;

/// <summary>
/// Options that specify the configuration section names from which AI deployments are loaded.
/// </summary>
public sealed class AIDeploymentCatalogOptions
{
    /// <summary>
    /// Gets the ordered list of configuration section names that are scanned for AI deployment definitions.
    /// </summary>
    public IList<string> DeploymentSections { get; } =
    [
        "CrestApps:AI:Deployments",
    ];
}
