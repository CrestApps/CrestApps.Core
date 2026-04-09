namespace CrestApps.Core.AI.Models;

public sealed class AIDeploymentCatalogOptions
{
    public IList<string> DeploymentSections { get; } =
    [
        "CrestApps:AI:Deployments",
        "CrestApps_AI:Deployments",
    ];
}
