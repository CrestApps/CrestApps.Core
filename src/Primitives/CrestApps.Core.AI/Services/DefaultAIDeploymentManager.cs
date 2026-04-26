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

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIDeploymentManager"/> class.
    /// </summary>
    /// <param name="deploymentStore">The deployment store.</param>
    /// <param name="handlers">The handlers.</param>
    /// <param name="deploymentSettings">The deployment settings.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAIDeploymentManager(
        IAIDeploymentStore deploymentStore,
        IEnumerable<ICatalogEntryHandler<AIDeployment>> handlers,
        IOptionsMonitor<DefaultAIDeploymentSettings> deploymentSettings,
        ILogger<DefaultAIDeploymentManager> logger)
        : base(deploymentStore, handlers, logger)
    {
        _deploymentSettings = deploymentSettings;
    }

    /// <summary>
    /// Gets default ai deployment settings.
    /// </summary>
    protected override ValueTask<DefaultAIDeploymentSettings> GetDefaultAIDeploymentSettingsAsync()
        => ValueTask.FromResult(_deploymentSettings.CurrentValue);
}
