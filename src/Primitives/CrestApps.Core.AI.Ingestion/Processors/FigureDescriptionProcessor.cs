using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// Transcribes the figures worth transcribing, so the values a document only prints inside a picture become
/// text an index can find. This is the only processor that calls a model.
/// </summary>
/// <remarks>
/// Retrieval is a vector search over text. A coefficient rasterized into a JPEG is not text, so a question
/// about it can never match the row that holds the answer. Describing at ingest is what puts it there.
/// <para>
/// Nothing here may fail an ingest. No vision deployment, a deployment that turns out not to accept images, a
/// call that throws — each is logged and the document continues as text only.
/// </para>
/// </remarks>
public sealed class FigureDescriptionProcessor : AIDocumentIngestionProcessor
{
    /// <summary>
    /// The version of the transcription prompt. It is part of the description cache key, so bumping it when
    /// the template changes is what stops an older transcription being served for a newer prompt.
    /// </summary>
    public const string FigureTranscriptionPromptVersion = "1";

    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IImageAnalysisService _imageAnalysisService;
    private readonly IFigureDescriptionCache _cache;
    private readonly ILogger<FigureDescriptionProcessor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FigureDescriptionProcessor"/> class.
    /// </summary>
    /// <param name="deploymentManager">The deployment manager used to resolve the vision deployment.</param>
    /// <param name="imageAnalysisService">The service that calls the vision model.</param>
    /// <param name="cache">The description cache.</param>
    /// <param name="logger">The logger.</param>
    public FigureDescriptionProcessor(
        IAIDeploymentManager deploymentManager,
        IImageAnalysisService imageAnalysisService,
        IFigureDescriptionCache cache,
        ILogger<FigureDescriptionProcessor> logger)
    {
        _deploymentManager = deploymentManager;
        _imageAnalysisService = imageAnalysisService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Describes every figure salience marked as worth describing.
    /// </summary>
    /// <param name="document">The document to process.</param>
    /// <param name="context">The per-run options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The processed document.</returns>
    public override async Task<IngestionDocument> ProcessAsync(
        IngestionDocument document,
        DocumentIngestionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        context ??= DocumentIngestionContext.Default;

        if (context.FigureMode == FigureProcessingMode.Off)
        {
            return document;
        }

        var describable = document.EnumerateContent()
            .OfType<IngestionDocumentImage>()
            .Where(image => string.Equals(image.GetMetadataString(FigureMetadataKeys.Tier), FigureTiers.Describe, StringComparison.Ordinal))
            .ToList();

        if (describable.Count == 0)
        {
            return document;
        }

        if (!context.DescribeFiguresInline)
        {
            // The indexer path backfills descriptions after the text is already searchable, so vision latency
            // never gates how soon a document can be found. The backfill resolves its own deployment, so
            // nothing is looked up here for a run that will not use it.
            return document;
        }

        var deployment = await ResolveVisionDeploymentAsync(context, cancellationToken);

        if (deployment == null)
        {
            // Information, not warning: a host with no vision deployment configured is a supported setup, not
            // a fault. One entry per document, not one per figure.
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Figure transcription skipped for '{Identifier}': no vision-capable deployment is available.",
                    document.Identifier);
            }

            return document;
        }

        var budget = context.MaxFigureDescriptionsPerDocument;
        var described = 0;

        foreach (var image in describable)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (budget >= 0 && described >= budget)
            {
                Demote(image);

                continue;
            }

            if (await TryDescribeAsync(document, image, deployment, context, cancellationToken))
            {
                described++;
            }
        }

        return document;
    }

    private async Task<bool> TryDescribeAsync(
        IngestionDocument document,
        IngestionDocumentImage image,
        AIDeployment deployment,
        DocumentIngestionContext context,
        CancellationToken cancellationToken)
    {
        var identifier = document.Identifier;
        var page = image.PageNumber;
        var contentHash = image.GetMetadataString(FigureMetadataKeys.ContentHash);
        var cached = await _cache.TryGetAsync(contentHash, FigureTranscriptionPromptVersion, cancellationToken);

        if (cached != null)
        {
            Apply(image, cached, deployment.Name);

            return false;
        }

        if (image.Content is not { } content || content.Length == 0)
        {
            Demote(image);

            return false;
        }

        ImageAnalysisResult result;

        try
        {
            result = await _imageAnalysisService.AnalyzeAsync(
                new ImageAnalysisRequest
                {
                    Content = content,
                    ContentType = image.MediaType ?? "image/png",
                    FileName = image.GetFigureId() ?? document.Identifier,
                    Caption = image.GetMetadataString(FigureMetadataKeys.Caption),
                    Context = image.GetMetadataString(FigureMetadataKeys.Context),
                    Language = context.Language,
                    TemplateId = AITemplateIds.FigureTranscription,
                    DeploymentName = context.VisionDeploymentName,
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Figure transcription failed for '{Identifier}' page {PageNumber}. The figure keeps its caption and the document continues.",
                identifier,
                page);

            Demote(image);

            return false;
        }

        var description = BuildDescription(result);

        if (description == null)
        {
            Demote(image);

            return true;
        }

        Apply(image, description, deployment.Name);

        await _cache.SetAsync(contentHash, FigureTranscriptionPromptVersion, description, cancellationToken);

        return true;
    }

    /// <summary>
    /// Joins the parts of a transcription that carry meaning. The description says what the figure is; the
    /// transcribed text is where a printed coefficient actually lives.
    /// </summary>
    /// <param name="result">The analysis result.</param>
    /// <returns>The description, or <see langword="null"/> when nothing usable came back.</returns>
    private static string BuildDescription(ImageAnalysisResult result)
    {
        if (result == null || !result.Success)
        {
            return null;
        }

        var parts = new[] { result.Description, result.OcrText }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return parts.Length == 0 ? null : string.Join('\n', parts);
    }

    private static void Apply(IngestionDocumentImage image, string description, string deploymentName)
    {
        image.AlternativeText = description;
        image.Metadata[FigureMetadataKeys.DescriptionSource] = "vision";
        image.Metadata[FigureMetadataKeys.DescriptionModel] = deploymentName;
        image.Metadata[FigureMetadataKeys.DescriptionPromptVersion] = FigureTranscriptionPromptVersion;
    }

    private static void Demote(IngestionDocumentImage image)
    {
        image.Metadata[FigureMetadataKeys.Tier] = FigureTiers.CaptionOnly;
    }

    /// <summary>
    /// Resolves the deployment that will read the figures, and confirms it can actually accept an image.
    /// </summary>
    /// <param name="context">The per-run options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The deployment, or <see langword="null"/> when none can read images.</returns>
    private async Task<AIDeployment> ResolveVisionDeploymentAsync(DocumentIngestionContext context, CancellationToken cancellationToken)
    {
        AIDeployment deployment;

        try
        {
            deployment = string.IsNullOrWhiteSpace(context.VisionDeploymentName)
                ? await _deploymentManager.ResolveSlotAsync(AIDeploymentSlotNames.Vision, cancellationToken: cancellationToken)
                : await _deploymentManager.FindByNameAsync(context.VisionDeploymentName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }

        // Slot resolution falls back to the first deployment capable of the slot's required feature, so a
        // non-null answer is not proof that this one accepts images. The capability is checked, never assumed.
        if (deployment == null ||
            !deployment.TryGet<AIDeploymentMetadata>(out var metadata) ||
            !metadata.SupportsFeature(AIDeploymentFeatureNames.ImageInput))
        {
            return null;
        }

        return deployment;
    }
}
