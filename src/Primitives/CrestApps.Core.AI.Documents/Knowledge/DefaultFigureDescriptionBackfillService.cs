using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Ingestion.Processors;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Knowledge;

/// <summary>
/// Finishes the figures ingestion left pending, one batch at a time.
/// </summary>
/// <remarks>
/// A figure the model answered about and could not read is marked failed and is never retried on its own.
/// Retrying a picture a model cannot read simply spends money on the same answer; an operator who has
/// changed something resets it.
/// <para>
/// A figure whose attempt never reached the model — a dropped connection, a rate limit — is left pending
/// instead, because that outcome says nothing about the picture and retiring it would lose it for good.
/// </para>
/// </remarks>
public sealed class DefaultFigureDescriptionBackfillService : IFigureDescriptionBackfillService
{
    private readonly IAIDataSourceStore _dataSourceStore;
    private readonly IKnowledgeObjectStore _store;
    private readonly IDocumentFileStore _fileStore;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IKnowledgeVisionDeploymentResolver _visionDeploymentResolver;
    private readonly IImageAnalysisService _imageAnalysisService;
    private readonly IAIDataSourceIndexingQueue _indexingQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KnowledgeIngestionOptions _options;
    private readonly ILogger<DefaultFigureDescriptionBackfillService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultFigureDescriptionBackfillService"/> class.
    /// </summary>
    /// <param name="dataSourceStore">The data source store.</param>
    /// <param name="store">The knowledge object store.</param>
    /// <param name="fileStore">The store the figure bytes were written to.</param>
    /// <param name="deploymentManager">The deployment manager used to resolve the vision deployment.</param>
    /// <param name="visionDeploymentResolver">The resolver that says which deployment an indexer chose.</param>
    /// <param name="imageAnalysisService">The service that calls the vision model.</param>
    /// <param name="indexingQueue">The indexing queue.</param>
    /// <param name="options">The knowledge ingestion options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="scopeFactory">
    /// Opens one scope per concurrent vision call, or <see langword="null"/> to run them one at a time.
    /// </param>
    public DefaultFigureDescriptionBackfillService(
        IAIDataSourceStore dataSourceStore,
        IKnowledgeObjectStore store,
        IDocumentFileStore fileStore,
        IAIDeploymentManager deploymentManager,
        IKnowledgeVisionDeploymentResolver visionDeploymentResolver,
        IImageAnalysisService imageAnalysisService,
        IAIDataSourceIndexingQueue indexingQueue,
        IOptions<KnowledgeIngestionOptions> options,
        ILogger<DefaultFigureDescriptionBackfillService> logger,
        IServiceScopeFactory scopeFactory = null)
    {
        _dataSourceStore = dataSourceStore;
        _store = store;
        _fileStore = fileStore;
        _deploymentManager = deploymentManager;
        _visionDeploymentResolver = visionDeploymentResolver;
        _imageAnalysisService = imageAnalysisService;
        _indexingQueue = indexingQueue;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> BackfillDueAsync(CancellationToken cancellationToken = default)
    {
        var dataSources = await _dataSourceStore.GetAllAsync(cancellationToken);
        var described = 0;

        // One data source at a time. Concurrency within a data source is bounded separately, so a host with
        // many ingested buckets cannot multiply its way past the vision budget.
        foreach (var dataSource in dataSources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(dataSource.Source, AIDataSourceSourceTypes.File, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            described += await BackfillAsync(dataSource, cancellationToken);
        }

        return described;
    }

    /// <inheritdoc />
    public async Task<int> BackfillAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var take = Math.Max(1, _options.BackfillBatchSize);
        var pending = await _store.GetByStatusAsync(dataSource.ItemId, KnowledgeObjectStatus.PendingDescription, take, cancellationToken);

        if (pending.Count == 0)
        {
            return 0;
        }

        var deployment = await ResolveVisionDeploymentAsync(null, cancellationToken);

        if (deployment == null)
        {
            // A host with no vision deployment is a supported setup. The figures stay pending and become
            // searchable by their captions; nothing fails and nothing is logged per figure.
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Figure backfill skipped for data source '{DataSourceId}': no vision-capable deployment is available.",
                    dataSource.ItemId);
            }

            return 0;
        }

        // Three stages, because only the middle one may run in parallel. Reading the store and writing to it
        // go through one session that is not safe to share between threads; the model calls are what take
        // the time, and they touch nothing but the network.
        var work = new List<FigureWork>(pending.Count);

        foreach (var entry in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            work.Add(await PrepareAsync(entry, deployment, cancellationToken));
        }

        // Two vision calls at once means two callers inside whatever the image analysis service depends on,
        // and in this library that reaches a scoped database context, which permits exactly one operation at
        // a time. Without a scope factory to isolate them, running them together turns a throughput setting
        // into an exception, so the work is serialized instead.
        var maxConcurrency = _scopeFactory == null ? 1 : Math.Max(1, _options.MaxConcurrentVisionCalls);

        using var concurrency = new SemaphoreSlim(maxConcurrency);

        await Task.WhenAll(work
            .Where(item => item.NeedsModelCall)
            .Select(item => TranscribeAsync(item, concurrency, cancellationToken)));

        var described = 0;
        var completed = new List<string>(work.Count);

        foreach (var item in work)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.IsTransient)
            {
                // Left exactly as it was found, which is what makes the next pass pick it up again.
                continue;
            }

            if (Record(item))
            {
                described++;
            }

            await _store.UpdateAsync(item.Entry, cancellationToken);

            completed.Add(item.Entry.CanonicalId);
        }

        if (completed.Count > 0)
        {
            await _indexingQueue.QueueSyncDataSourceDocumentsAsync(dataSource.ItemId, completed, cancellationToken);
        }

        return described;
    }

    /// <summary>
    /// Gathers everything one figure needs before any model is called: its details, the deployment its
    /// indexer chose, a description already produced from identical bytes, and its picture.
    /// </summary>
    /// <param name="entry">The pending figure.</param>
    /// <param name="fallback">The host's vision deployment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The prepared work item.</returns>
    private async Task<FigureWork> PrepareAsync(KnowledgeObject entry, AIDeployment fallback, CancellationToken cancellationToken)
    {
        if (!entry.TryGet<FigureDetails>(out var details))
        {
            details = new FigureDetails();
        }

        // The figure's own indexer may have chosen a different model from the host's. Resolved per figure
        // because two indexers can feed one data source.
        var deployment = await ResolveFigureDeploymentAsync(entry, fallback, cancellationToken);
        var item = new FigureWork(entry, details, deployment);

        // An identical picture already transcribed under the current prompt is copied rather than paid for
        // again. A magazine that prints the same chart in two issues costs one call, not two.
        item.Description = await FindCachedDescriptionAsync(entry, cancellationToken);

        if (item.Description != null)
        {
            return item;
        }

        item.Content = await ReadFigureBytesAsync(entry);

        if (item.Content.Length == 0)
        {
            item.Error = "The stored figure could not be read.";
        }

        return item;
    }

    /// <summary>
    /// Calls the vision model for one figure, within the configured concurrency.
    /// </summary>
    /// <param name="item">The prepared work item.</param>
    /// <param name="concurrency">The gate on simultaneous model calls.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task TranscribeAsync(FigureWork item, SemaphoreSlim concurrency, CancellationToken cancellationToken)
    {
        await concurrency.WaitAsync(cancellationToken);

        // A scope per call is what makes more than one call at a time safe: each gets its own analysis
        // service, and so its own database context, rather than sharing the one this service was built in.
        using var scope = _scopeFactory?.CreateScope();
        var analysisService = scope == null
            ? _imageAnalysisService
            : scope.ServiceProvider.GetService<IImageAnalysisService>() ?? _imageAnalysisService;

        try
        {
            var result = await analysisService.AnalyzeAsync(
                new ImageAnalysisRequest
                {
                    Content = item.Content,
                    ContentType = string.IsNullOrWhiteSpace(item.Entry.MediaType) ? "image/png" : item.Entry.MediaType,
                    FileName = item.Entry.CanonicalId,
                    Caption = item.Details.Caption,
                    Context = item.Details.Context,
                    Language = item.Entry.Language,
                    TemplateId = AITemplateIds.FigureTranscription,
                    DeploymentName = item.Deployment.Name,
                },
                cancellationToken);

            item.Description = BuildDescription(result);

            if (item.Description == null)
            {
                item.Error = result?.Error ?? "The vision model returned nothing usable.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            item.Error = ex.Message;
            item.IsTransient = IsTransport(ex);

            if (item.IsTransient)
            {
                // The model was never reached, so this says nothing about whether the picture is readable.
                // A figure marked failed is never retried, and retiring one over a dropped connection would
                // lose it permanently, so it stays pending for the next pass instead.
                _logger.LogWarning(ex, "Figure transcription could not be attempted for '{CanonicalId}'. It stays pending.", item.Entry.CanonicalId);
            }
            else
            {
                _logger.LogWarning(ex, "Figure transcription failed for '{CanonicalId}'. The figure keeps its caption.", item.Entry.CanonicalId);
            }
        }
        finally
        {
            concurrency.Release();
        }
    }

    /// <summary>
    /// Records the outcome of one figure on the object, whichever way it went.
    /// </summary>
    /// <param name="item">The work item.</param>
    /// <returns><see langword="true"/> when a description was recorded.</returns>
    /// <summary>
    /// Says whether a failure happened on the way to the model rather than at it.
    /// </summary>
    /// <param name="exception">The exception the attempt threw.</param>
    /// <returns><see langword="true"/> when the attempt never produced an answer about the picture.</returns>
    /// <remarks>
    /// The distinction decides whether a figure is retired or retried. A model that looked at the picture
    /// and refused it will refuse it again, so that figure is retired; a connection that dropped or a
    /// request that timed out says nothing about the picture, so that figure is left for the next pass.
    /// </remarks>
    private static bool IsTransport(Exception exception)
    {
        return exception switch
        {
            HttpRequestException => true,
            TimeoutException => true,
            TaskCanceledException => true,
            _ => false,
        };
    }

    private static bool Record(FigureWork item)
    {
        if (item.Description != null)
        {
            Apply(item.Entry, item.Details, item.Description, item.Deployment.Name);

            return true;
        }

        Fail(item.Entry, item.Details, item.Error ?? "The figure was not transcribed.");

        return false;
    }

    /// <summary>
    /// Looks for a transcription of the identical picture produced under the current prompt.
    /// </summary>
    /// <param name="entry">The pending figure.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The description, or <see langword="null"/> when there is none.</returns>
    private async Task<string> FindCachedDescriptionAsync(KnowledgeObject entry, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entry.ContentHash))
        {
            return null;
        }

        try
        {
            var match = await _store.FindFigureByContentHashAsync(
                entry.ContentHash,
                FigureDescriptionProcessor.FigureTranscriptionPromptVersion,
                cancellationToken);

            if (match == null || match.CanonicalId == entry.CanonicalId || !match.TryGet<FigureDetails>(out var details))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(details.Description) ? null : details.Description;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A cache that cannot be read costs one model call, never a transcription.
            _logger.LogWarning(ex, "Failed to look up a cached description for '{CanonicalId}'.", entry.CanonicalId);

            return null;
        }
    }

    private async Task<ReadOnlyMemory<byte>> ReadFigureBytesAsync(KnowledgeObject entry)
    {
        if (string.IsNullOrWhiteSpace(entry.StoragePath))
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        try
        {
            await using var stream = await _fileStore.GetFileAsync(entry.StoragePath);

            if (stream is null)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);

            return buffer.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the stored figure '{StoragePath}'.", entry.StoragePath);

            return ReadOnlyMemory<byte>.Empty;
        }
    }

    /// <summary>
    /// Resolves the deployment for one figure, preferring the one its indexer chose.
    /// </summary>
    /// <param name="entry">The figure.</param>
    /// <param name="fallback">The deployment to use when the indexer chose none or its choice cannot read images.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The deployment.</returns>
    private async Task<AIDeployment> ResolveFigureDeploymentAsync(KnowledgeObject entry, AIDeployment fallback, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entry.IndexerId))
        {
            return fallback;
        }

        string configured;

        try
        {
            configured = await _visionDeploymentResolver.ResolveAsync(entry.IndexerId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the vision deployment configured for indexer '{IndexerId}'.", entry.IndexerId);

            return fallback;
        }

        if (string.IsNullOrWhiteSpace(configured))
        {
            return fallback;
        }

        // A configured deployment that cannot read an image is not a cheaper choice, it is a setting that
        // would silently transcribe nothing. The host's own is used instead, and the figure still gets read.
        return await ResolveVisionDeploymentAsync(configured, cancellationToken) ?? fallback;
    }

    /// <summary>
    /// Resolves the deployment that will read the figures, and confirms it can actually accept an image.
    /// </summary>
    /// <param name="deploymentName">The deployment to use, or <see langword="null"/> to resolve the host's.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The deployment, or <see langword="null"/> when none can read images.</returns>
    private async Task<AIDeployment> ResolveVisionDeploymentAsync(string deploymentName, CancellationToken cancellationToken)
    {
        AIDeployment deployment;
        var name = string.IsNullOrWhiteSpace(deploymentName) ? _options.VisionDeploymentName : deploymentName;

        try
        {
            deployment = string.IsNullOrWhiteSpace(name)
                ? await _deploymentManager.ResolveSlotAsync(AIDeploymentSlotNames.Vision, cancellationToken: cancellationToken)
                : await _deploymentManager.FindByNameAsync(name, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }

        if (deployment == null ||
            !deployment.TryGet<AIDeploymentMetadata>(out var metadata) ||
            !metadata.SupportsFeature(AIDeploymentFeatureNames.ImageInput))
        {
            return null;
        }

        return deployment;
    }

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

    private static void Apply(KnowledgeObject entry, FigureDetails details, string description, string deploymentName)
    {
        details.Description = description;
        details.DescriptionModel = deploymentName;
        details.DescriptionPromptVersion = FigureDescriptionProcessor.FigureTranscriptionPromptVersion;
        details.Error = null;

        entry.Put(details);
        entry.Content = KnowledgeObjectBuilder.BuildFigureContent(details.Caption, details.Context, description);
        entry.Status = KnowledgeObjectStatus.Ready;
    }

    private static void Fail(KnowledgeObject entry, FigureDetails details, string error)
    {
        details.Error = error;

        entry.Put(details);
        entry.Status = KnowledgeObjectStatus.Failed;
    }

    /// <summary>
    /// One figure on its way through the backfill.
    /// </summary>
    private sealed class FigureWork
    {
        public FigureWork(KnowledgeObject entry, FigureDetails details, AIDeployment deployment)
        {
            Entry = entry;
            Details = details;
            Deployment = deployment;
        }

        public KnowledgeObject Entry { get; }

        public FigureDetails Details { get; }

        public AIDeployment Deployment { get; }

        public ReadOnlyMemory<byte> Content { get; set; }

        public string Description { get; set; }

        public string Error { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the attempt failed before the model answered, in which
        /// case the figure is left pending rather than retired.
        /// </summary>
        public bool IsTransient { get; set; }

        /// <summary>
        /// Gets a value indicating whether the model still has to be asked: nothing was cached and the
        /// picture could be read.
        /// </summary>
        public bool NeedsModelCall => Description == null && Error == null;
    }
}
