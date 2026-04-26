using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the default AI Deployment Manager.
/// </summary>
public sealed class DefaultAIDeploymentManager : AIDeploymentManagerBase
{
    private readonly IOptionsMonitor<DefaultAIDeploymentSettings> _deploymentSettings;

    public DefaultAIDeploymentManager(
        IAIDeploymentStore deploymentStore,
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
