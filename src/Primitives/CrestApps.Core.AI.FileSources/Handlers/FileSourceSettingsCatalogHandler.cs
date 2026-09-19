using System.ComponentModel.DataAnnotations;
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.FileSources.Handlers;

/// <summary>
/// Refuses ingestion settings that would silently do nothing.
/// </summary>
/// <typeparam name="T">The kind of record carrying the settings.</typeparam>
/// <remarks>
/// A deployment that cannot accept an image is not a cheaper way to transcribe figures: it is a setting that
/// transcribes none of them and says nothing about it. The same goes for an embedding deployment that
/// produces no embeddings. Both are caught when they are saved, where someone is there to read the message.
/// <para>
/// Leaving a deployment unset is always valid and means "use the host's". Refusing that would make every
/// source carry a model choice nobody wanted to make.
/// </para>
/// <para>
/// It is generic because the settings it validates live on an <see cref="IngestionSource"/>, and a web
/// crawler pointed at an ingested data source carries them too. Registering it once per record type keeps
/// one screen from being validated and the other not.
/// </para>
/// </remarks>
public sealed class FileSourceSettingsCatalogHandler<T> : CatalogEntryHandlerBase<T>
    where T : IngestionSource
{
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIDeploymentCapabilityService _capabilityService;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSourceSettingsCatalogHandler{T}"/> class.
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
    /// Validates the ingestion settings on a record as it is saved.
    /// </summary>
    /// <param name="context">The validating context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task ValidatingAsync(ValidatingContext<T> context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Model.TryGet<FileSourceMetadata>(out var metadata))
        {
            return;
        }

        await ValidateAsync(
            context,
            metadata.VisionDeploymentName,
            AIDeploymentFeatureNames.ImageInput,
            nameof(FileSourceMetadata.VisionDeploymentName),
            "The selected deployment does not accept image input.",
            cancellationToken);
    }

    private async Task ValidateAsync(
        ValidatingContext<T> context,
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
