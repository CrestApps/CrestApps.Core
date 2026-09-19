using CrestApps.Core.AI.Ingestion.Knowledge;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Ingestion;

/// <summary>
/// Answers with the vision deployment the figure's own source was configured with.
/// </summary>
/// <remarks>
/// A figure is transcribed after ingestion, from a stored object that carries only the identifier of the
/// record that produced it. Without this the per-source choice would take effect for everything except the
/// one thing it exists to control.
/// <para>
/// Which kind of record that identifier names is not this resolver's concern: it asks each contributed
/// <see cref="IIngestionSourceProvider"/> in turn, so a host that enabled one feature and not the other
/// resolves the sources it has and is not asked for a store it never registered.
/// </para>
/// </remarks>
public sealed class IngestionSourceVisionDeploymentResolver : IKnowledgeVisionDeploymentResolver
{
    private readonly IEnumerable<IIngestionSourceProvider> _sourceProviders;
    private readonly ILogger<IngestionSourceVisionDeploymentResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionSourceVisionDeploymentResolver"/> class.
    /// </summary>
    /// <param name="sourceProviders">The contributed access to each feature's own source records.</param>
    /// <param name="logger">The logger.</param>
    public IngestionSourceVisionDeploymentResolver(
        IEnumerable<IIngestionSourceProvider> sourceProviders,
        ILogger<IngestionSourceVisionDeploymentResolver> logger)
    {
        _sourceProviders = sourceProviders;
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
            IngestionSource ingestionSource = null;

            foreach (var provider in _sourceProviders)
            {
                ingestionSource = await provider.FindByIdAsync(fileSourceId, cancellationToken);

                if (ingestionSource is not null)
                {
                    break;
                }
            }

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
