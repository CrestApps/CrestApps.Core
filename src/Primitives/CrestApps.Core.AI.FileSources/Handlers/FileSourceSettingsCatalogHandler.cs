using System.ComponentModel.DataAnnotations;
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.FileSources.Handlers;

/// <summary>
/// Refuses indexer settings that would silently do nothing.
/// </summary>
/// <remarks>
/// A deployment that cannot accept an image is not a cheaper way to transcribe figures: it is a setting that
/// transcribes none of them and says nothing about it. The same goes for an embedding deployment that
/// produces no embeddings. Both are caught when they are saved, where someone is there to read the message.
/// <para>
/// Leaving a deployment unset is always valid and means "use the host's". Refusing that would make every
/// indexer carry a model choice nobody wanted to make.
/// </para>
/// </remarks>
public sealed class FileSourceSettingsCatalogHandler : CatalogEntryHandlerBase<WebCrawler>
{
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIDeploymentCapabilityService _capabilityService;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSourceSettingsCatalogHandler"/> class.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="capabilityService">The capability service.</param>
    public FileSourceSettingsCatalogHandler(
        IAIDeploymentManager deploymentManager,
        IAIDeploymentCapabilityService capabilityService)
    {
        _deploymentManager = deploymentManager;
        _capabilityService = capabilityService;
    }

    /// <summary>
    /// Validates the ingestion settings on an indexer as it is saved.
    /// </summary>
    /// <param name="context">The validating context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task ValidatingAsync(ValidatingContext<WebCrawler> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Model.TryGet<IndexerMetadata>(out var metadata))
        {
            return;
        }

        await ValidateAsync(
            context,
            metadata.VisionDeploymentName,
            AIDeploymentFeatureNames.ImageInput,
            nameof(IndexerMetadata.VisionDeploymentName),
            "The selected deployment does not accept image input.",
            cancellationToken);
    }

    private async Task ValidateAsync(
        ValidatingContext<WebCrawler> context,
        string deploymentName,
        string feature,
        string field,
        string message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deploymentName))
        {
            // Null means "use the host's", never "disable". FigureMode is how figure transcription is
            // turned off.
            return;
        }

        var deployment = await _deploymentManager.FindByNameAsync(deploymentName, cancellationToken);

        if (deployment is null)
        {
            context.Result.Fail(new ValidationResult("The selected deployment could not be found.", [field]));

            return;
        }

        if (!_capabilityService.SupportsFeatureOrUnconstrained(deployment, feature))
        {
            context.Result.Fail(new ValidationResult(message, [field]));
        }
    }
}
