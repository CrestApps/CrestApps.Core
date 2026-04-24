using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// A generic AI completion client that derives its client name from a
/// <typeparamref name="TProvider"/> marker type, eliminating the need for
/// provider-specific subclass files.
/// </summary>
/// <typeparam name="TProvider">
/// A marker type implementing <see cref="IAIProviderMarker"/> that supplies the client name.
/// </typeparam>
public sealed class ProviderAICompletionClient<TProvider> : NamedAICompletionClient
    where TProvider : IAIProviderMarker
{
    public ProviderAICompletionClient(
        IAIClientFactory aIClientFactory,
        ILoggerFactory loggerFactory,
        IDistributedCache distributedCache,
        IServiceProvider serviceProvider,
        IEnumerable<IAICompletionServiceHandler> handlers,
        IOptions<DefaultAIOptions> defaultOptions,
        ITemplateService aiTemplateService,
        IAIDeploymentManager deploymentManager)
        : base(TProvider.ClientName, aIClientFactory, distributedCache, loggerFactory, serviceProvider, defaultOptions.Value, handlers, aiTemplateService, deploymentManager)
    {
    }
}
