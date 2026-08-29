namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// A base class for documentation sources that materialize a full <see cref="DocumentationCorpus"/>
/// and answer queries from it. The corpus is built lazily on first use and cached in memory until it
/// expires, so derived sources only implement how the corpus is produced.
/// </summary>
public abstract class CachingDocumentationSource : IDocumentationSource
{
    /// <summary>
    /// How long an empty corpus is cached before the source retries. An empty result usually means the
    /// site or its sitemap was temporarily unreachable, so it is cached only briefly rather than for the
    /// full <see cref="_cacheDuration"/> to allow a quick recovery.
    /// </summary>
    private static readonly TimeSpan _emptyResultCacheDuration = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _cacheDuration;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private DocumentationCorpus _corpus;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingDocumentationSource"/> class.
    /// </summary>
    /// <param name="name">The unique logical name of the source.</param>
    /// <param name="cacheDuration">How long the built corpus is cached before it is refreshed.</param>
    /// <param name="timeProvider">The time provider.</param>
    protected CachingDocumentationSource(string name, TimeSpan cacheDuration, TimeProvider timeProvider)
    {
        Name = name;
        _cacheDuration = cacheDuration;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Gets the maximum number of results this source contributes to a search.
    /// </summary>
    protected abstract int MaxResults { get; }

    /// <summary>
    /// Builds the documentation corpus that backs this source.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The materialized corpus.</returns>
    protected abstract Task<DocumentationCorpus> BuildCorpusAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentationSearchResult>> SearchAsync(DocumentationSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return [];
        }

        var corpus = await GetCorpusAsync(cancellationToken);

        return corpus.Search(request.Query, Name, Math.Min(request.MaxResults, MaxResults));
    }

    private async Task<DocumentationCorpus> GetCorpusAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        if (!IsExpired(now))
        {
            return _corpus;
        }

        await _loadLock.WaitAsync(cancellationToken);

        try
        {
            if (!IsExpired(_timeProvider.GetUtcNow()))
            {
                return _corpus;
            }

            _corpus = await BuildCorpusAsync(cancellationToken);
            _loadedAt = _timeProvider.GetUtcNow();

            return _corpus;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private bool IsExpired(DateTimeOffset now)
    {
        if (_corpus is null)
        {
            return true;
        }

        // An empty corpus is treated as a likely transient failure and refreshed sooner.
        var duration = _corpus.Count == 0 && _emptyResultCacheDuration < _cacheDuration
            ? _emptyResultCacheDuration
            : _cacheDuration;

        return now - _loadedAt >= duration;
    }
}
