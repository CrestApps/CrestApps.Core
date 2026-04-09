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

    protected override ValueTask<string> GetGlobalDefaultSelectorAsync(AIDeploymentType type)
    {
        var settings = _deploymentSettings.CurrentValue;

        var result = type switch
        {
            AIDeploymentType.Chat => settings.DefaultChatDeploymentName,
            AIDeploymentType.Utility => settings.DefaultUtilityDeploymentName,
            AIDeploymentType.Embedding => settings.DefaultEmbeddingDeploymentName,
            AIDeploymentType.Image => settings.DefaultImageDeploymentName,
            AIDeploymentType.SpeechToText => settings.DefaultSpeechToTextDeploymentName,
            AIDeploymentType.TextToSpeech => settings.DefaultTextToSpeechDeploymentName,
            _ => null,
        };

        return new ValueTask<string>(result);
    }
}
