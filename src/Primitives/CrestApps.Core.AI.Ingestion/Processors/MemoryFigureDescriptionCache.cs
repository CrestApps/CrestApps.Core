using Microsoft.Extensions.Caching.Memory;

namespace CrestApps.Core.AI.Ingestion.Processors;

/// <summary>
/// Keeps descriptions in process memory for as long as the process lives.
/// </summary>
public sealed class MemoryFigureDescriptionCache : IFigureDescriptionCache
{
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryFigureDescriptionCache"/> class.
    /// </summary>
    /// <param name="cache">The backing memory cache.</param>
    public MemoryFigureDescriptionCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Looks up a stored description.
    /// </summary>
    /// <param name="contentHash">The hash of the figure bytes.</param>
    /// <param name="promptVersion">The version of the transcription prompt.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The description, or <see langword="null"/> when nothing is stored for it.</returns>
    public Task<string> TryGetAsync(string contentHash, string promptVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(contentHash))
        {
            return Task.FromResult<string>(null);
        }

        return Task.FromResult(_cache.TryGetValue<string>(BuildKey(contentHash, promptVersion), out var description)
            ? description
            : null);
    }

    /// <summary>
    /// Stores a description.
    /// </summary>
    /// <param name="contentHash">The hash of the figure bytes.</param>
    /// <param name="promptVersion">The version of the transcription prompt.</param>
    /// <param name="description">The description.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task SetAsync(string contentHash, string promptVersion, string description, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(contentHash) || string.IsNullOrWhiteSpace(description))
        {
            return Task.CompletedTask;
        }

        _cache.Set(BuildKey(contentHash, promptVersion), description);

        return Task.CompletedTask;
    }

    private static string BuildKey(string contentHash, string promptVersion)
    {
        return $"figure-description:{contentHash}:{promptVersion}";
    }
}
