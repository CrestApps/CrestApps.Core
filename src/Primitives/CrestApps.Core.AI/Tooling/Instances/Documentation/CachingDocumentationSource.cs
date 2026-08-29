namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// A base class for documentation sources that materialize a full <see cref="DocumentationCorpus"/>
/// and answer queries from it. The corpus is built lazily on first use and cached in memory until it
/// expires, so derived sources only implement how the corpus is produced.
/// </summary>
/// <remarks>
/// Building the corpus (crawling a site or downloading an index) can be slow the first time. To keep a
/// slow first search from blocking — and, in a tool-calling loop, from being retried until the model
/// exhausts its iteration budget — the build runs in the background and a search waits only up to a
/// bounded budget. If the corpus is not ready in time, the search throws
/// <see cref="DocumentationIndexPendingException"/> while the build keeps running, so a subsequent search
/// succeeds from the warmed cache.
/// </remarks>
public abstract class CachingDocumentationSource : IDocumentationSource
{
    /// <summary>
    /// How long an empty corpus is cached before the source retries. An empty result usually means the
    /// site or its sitemap was temporarily unreachable, so it is cached only briefly rather than for the
    /// full <see cref="_cacheDuration"/> to allow a quick recovery.
    /// </summary>
    private static readonly TimeSpan _emptyResultCacheDuration = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _cacheDuration;
    private readonly TimeSpan _buildWaitBudget;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private DocumentationCorpus _corpus;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    private Task<DocumentationCorpus> _buildTask;
    private DateTimeOffset _buildStartedAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingDocumentationSource"/> class.
    /// </summary>
    /// <param name="name">The unique logical name of the source.</param>
    /// <param name="cacheDuration">How long the built corpus is cached before it is refreshed.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="buildWaitBudget">
    /// The longest a search waits for a not-yet-ready corpus before reporting the index as pending while
    /// the build continues in the background.
    /// </param>
    protected CachingDocumentationSource(string name, TimeSpan cacheDuration, TimeProvider timeProvider, TimeSpan buildWaitBudget)
    {
        Name = name;
        _cacheDuration = cacheDuration;
        _timeProvider = timeProvider;
        _buildWaitBudget = buildWaitBudget < TimeSpan.Zero ? TimeSpan.Zero : buildWaitBudget;
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
        if (!IsExpired(_timeProvider.GetUtcNow()))
        {
            return _corpus;
        }

        Task<DocumentationCorpus> build;
        DateTimeOffset startedAt;

        await _loadLock.WaitAsync(cancellationToken);

        try
        {
            if (!IsExpired(_timeProvider.GetUtcNow()))
            {
                return _corpus;
            }

            // Start the build once and share it. It runs on an uncancellable token so a caller that gives
            // up (or whose request is cancelled) still leaves the corpus warming for the next search.
            if (_buildTask is null)
            {
                _buildStartedAt = _timeProvider.GetUtcNow();
                _buildTask = BuildAndCacheAsync();
            }

            build = _buildTask;
            startedAt = _buildStartedAt;
        }
        finally
        {
            _loadLock.Release();
        }

        if (build.IsCompleted)
        {
            return await build;
        }

        // Wait only for what remains of the budget since the build began, so repeated searches during one
        // slow build do not each wait the full budget.
        var remaining = _buildWaitBudget - (_timeProvider.GetUtcNow() - startedAt);

        if (remaining > TimeSpan.Zero)
        {
            var finished = await Task.WhenAny(build, Task.Delay(remaining, cancellationToken));

            if (finished == build)
            {
                return await build;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        throw new DocumentationIndexPendingException(Name);
    }

    private async Task<DocumentationCorpus> BuildAndCacheAsync()
    {
        try
        {
            var corpus = await BuildCorpusAsync(CancellationToken.None);

            await _loadLock.WaitAsync();

            try
            {
                _corpus = corpus;
                _loadedAt = _timeProvider.GetUtcNow();
                _buildTask = null;
            }
            finally
            {
                _loadLock.Release();
            }

            return corpus;
        }
        catch
        {
            // Let the next search start a fresh build rather than latching onto the failed one.
            await _loadLock.WaitAsync();

            try
            {
                _buildTask = null;
            }
            finally
            {
                _loadLock.Release();
            }

            throw;
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
