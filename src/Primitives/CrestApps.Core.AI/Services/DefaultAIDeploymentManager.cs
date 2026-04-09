using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

public class DefaultAIDeploymentManager : AIDeploymentManagerBase
{
    private readonly IOptionsMonitor<DefaultAIDeploymentSettings> _deploymentSettings;

    public DefaultAIDeploymentManager(
        INamedSourceCatalog<AIDeployment> deploymentStore,
        IEnumerable<ICatalogEntryHandler<AIDeployment>> handlers,
        IOptionsMonitor<DefaultAIDeploymentSettings> deploymentSettings,
        ILogger<DefaultAIDeploymentManager> logger)
        : base(deploymentStore, handlers, logger)
    {
        _deploymentSettings = deploymentSettings;
    }

    protected override ValueTask<DefaultAIDeploymentSettings> GetDefaultAIDeploymentSettingsAsync()
        => ValueTask.FromResult(_deploymentSettings.CurrentValue);
}
