using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tabular;

internal sealed class TabularWorkspaceCache : ITabularWorkspaceInvalidator, IDisposable
{
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _syncRoot = new();
    private readonly TabularWorkspaceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TabularWorkspaceCache> _logger;
    private bool _disposed;

    public TabularWorkspaceCache(
        IOptions<TabularWorkspaceOptions> options,
        TimeProvider timeProvider,
        ILogger<TabularWorkspaceCache> logger)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public TabularWorkspace GetOrCreate(TabularWorkspaceCacheKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_syncRoot)
        {
            ThrowIfDisposed();

            var now = _timeProvider.GetUtcNow();

            if (_entries.TryGetValue(key.Value, out var existing))
            {
                existing.LastAccessUtc = now;

                return existing.Workspace;
            }

            var workspace = new TabularWorkspace(_options);
            _entries[key.Value] = new CacheEntry(key, workspace, now);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Created tabular workspace cache entry '{CacheKey}'.", key.Value);
            }

            return workspace;
        }
    }

    public int CompactExpired()
    {
        var expiration = ResolveSlidingExpiration();

        if (expiration <= TimeSpan.Zero)
        {
            return 0;
        }

        var now = _timeProvider.GetUtcNow();

        return RemoveWhere(entry => now - entry.LastAccessUtc >= expiration);
    }

    public void InvalidateReference(string referenceType, string referenceId)
    {
        if (string.IsNullOrWhiteSpace(referenceType) || string.IsNullOrWhiteSpace(referenceId))
        {
            return;
        }

        RemoveWhere(entry => entry.Key.References.Any(reference =>
            string.Equals(reference.ReferenceType, referenceType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(reference.ReferenceId, referenceId, StringComparison.OrdinalIgnoreCase)));
    }

    public void InvalidateChatInteraction(string chatInteractionId)
    {
        if (string.IsNullOrWhiteSpace(chatInteractionId))
        {
            return;
        }

        RemoveWhere(entry => string.Equals(entry.Key.ChatInteractionId, chatInteractionId, StringComparison.OrdinalIgnoreCase));
    }

    public void InvalidateChatSession(string chatSessionId)
    {
        if (string.IsNullOrWhiteSpace(chatSessionId))
        {
            return;
        }

        RemoveWhere(entry => string.Equals(entry.Key.ChatSessionId, chatSessionId, StringComparison.OrdinalIgnoreCase));
    }

    public void InvalidateProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        RemoveWhere(entry => string.Equals(entry.Key.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        List<CacheEntry> entries;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            entries = _entries.Values.ToList();
            _entries.Clear();
        }

        DisposeEntries(entries);
    }

    private int RemoveWhere(Func<CacheEntry, bool> predicate)
    {
        List<CacheEntry> removed;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return 0;
            }

            removed = _entries.Values.Where(predicate).ToList();

            foreach (var entry in removed)
            {
                _entries.Remove(entry.Key.Value);
            }
        }

        DisposeEntries(removed);

        return removed.Count;
    }

    private static void DisposeEntries(IEnumerable<CacheEntry> entries)
    {
        foreach (var entry in entries)
        {
            entry.Workspace.Dispose();
        }
    }

    private TimeSpan ResolveSlidingExpiration()
    {
        return _options.WorkspaceSlidingExpiration <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(5)
            : _options.WorkspaceSlidingExpiration;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class CacheEntry
    {
        public CacheEntry(
            TabularWorkspaceCacheKey key,
            TabularWorkspace workspace,
            DateTimeOffset lastAccessUtc)
        {
            Key = key;
            Workspace = workspace;
            LastAccessUtc = lastAccessUtc;
        }

        public TabularWorkspaceCacheKey Key { get; }

        public TabularWorkspace Workspace { get; }

        public DateTimeOffset LastAccessUtc { get; set; }
    }
}
