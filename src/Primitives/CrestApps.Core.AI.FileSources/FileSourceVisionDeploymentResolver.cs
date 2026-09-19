using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Ingestion.Knowledge;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.FileSources;

/// <summary>
/// Answers with the vision deployment the figure's own source was configured with.
/// </summary>
/// <remarks>
/// A figure is transcribed after ingestion, from a stored object that carries only the identifier of the
/// record that produced it. Without this the per-source choice would take effect for everything except the
/// one thing it exists to control.
/// <para>
/// The identifier names a <see cref="FileSource"/> in almost every case, but a <see cref="WebCrawler"/>
/// pointed at an ingested data source produces figures the same way, so both stores are asked.
/// </para>
/// </remarks>
public sealed class FileSourceVisionDeploymentResolver : IKnowledgeVisionDeploymentResolver
{
    private readonly IFileSourceStore _fileSourceStore;
    private readonly IWebCrawlerStore _webCrawlerStore;
    private readonly ILogger<FileSourceVisionDeploymentResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSourceVisionDeploymentResolver"/> class.
    /// </summary>
    /// <param name="fileSourceStore">The store file sources live in.</param>
    /// <param name="webCrawlerStore">The store web crawlers live in.</param>
    /// <param name="logger">The logger.</param>
    public FileSourceVisionDeploymentResolver(
        IFileSourceStore fileSourceStore,
        IWebCrawlerStore webCrawlerStore,
        ILogger<FileSourceVisionDeploymentResolver> logger)
    {
        _fileSourceStore = fileSourceStore;
        _webCrawlerStore = webCrawlerStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> ResolveAsync(string fileSourceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileSourceId))
        {
            return null;
        }

        try
        {
            IngestionSource ingestionSource = await _fileSourceStore.FindByIdAsync(fileSourceId, cancellationToken);

            ingestionSource ??= await _webCrawlerStore.FindByIdAsync(fileSourceId, cancellationToken);

            if (ingestionSource is null || !ingestionSource.TryGet<FileSourceMetadata>(out var metadata))
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
            // A source that cannot be read costs the host's own deployment, never a transcription.
            _logger.LogWarning(ex, "Failed to read ingestion source '{SourceId}'.", fileSourceId);

            return null;
        }
    }
}
