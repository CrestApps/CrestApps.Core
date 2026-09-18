using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Documents.Knowledge;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// Answers with the vision deployment the figure's own indexer was configured with.
/// </summary>
/// <remarks>
/// A figure is transcribed after ingestion, from a stored object that carries only the indexer's identifier.
/// Without this the per-indexer choice would take effect for everything except the one thing it exists to
/// control.
/// </remarks>
public sealed class FileSourceVisionDeploymentResolver : IKnowledgeVisionDeploymentResolver
{
    private readonly IWebCrawlerStore _indexerStore;
    private readonly ILogger<FileSourceVisionDeploymentResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSourceVisionDeploymentResolver"/> class.
    /// </summary>
    /// <param name="indexerStore">The store the indexers live in.</param>
    /// <param name="logger">The logger.</param>
    public FileSourceVisionDeploymentResolver(
        IWebCrawlerStore indexerStore,
        ILogger<FileSourceVisionDeploymentResolver> logger)
    {
        _indexerStore = indexerStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ResolveAsync(string indexerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(indexerId))
        {
            return null;
        }

        try
        {
            var indexer = await _indexerStore.FindByIdAsync(indexerId, cancellationToken);

            if (indexer is null || !indexer.TryGet<IndexerMetadata>(out var metadata))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(metadata.VisionDeploymentName) ? null : metadata.VisionDeploymentName;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // An indexer that cannot be read costs the host's own deployment, never a transcription.
            _logger.LogWarning(ex, "Failed to read indexer '{IndexerId}'.", indexerId);

            return null;
        }
    }
}
