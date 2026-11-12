namespace CrestApps.Core.AI.Documents.Endpoints;

/// <summary>
/// Serializes the uploads to one chat session within this process. An upload reads the session's document list, adds to it
/// and saves it, so two uploads that overlap each save their own copy and the last one wins: the cap on documents is
/// overshot, and the documents of the other upload stay stored under the session without being listed on it, where
/// nothing can remove them. Uploads to different sessions do not wait for each other.
/// </summary>
/// <remarks>
/// The lock is per process. A host that runs several instances behind a load balancer and needs the cap to hold across
/// them also needs a store that checks concurrency, or sticky routing for a session.
/// </remarks>
internal static class ChatSessionLocks
{
    private static readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Waits for the lock of the session and returns what releases it.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lock, to dispose when the upload, including its commit, is done.</returns>
    public static async Task<IDisposable> AcquireAsync(string sessionId, CancellationToken cancellationToken)
    {
        Entry entry;

        lock (_entries)
        {
            if (!_entries.TryGetValue(sessionId, out entry))
            {
                entry = new Entry();
                _entries[sessionId] = entry;
            }

            entry.Users++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Leave(sessionId, entry);

            throw;
        }

        return new Releaser(sessionId, entry);
    }

    private static void Leave(string sessionId, Entry entry)
    {
        lock (_entries)
        {
            if (--entry.Users == 0)
            {
                _entries.Remove(sessionId);
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int Users { get; set; }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly string _sessionId;
        private Entry _entry;

        public Releaser(string sessionId, Entry entry)
        {
            _sessionId = sessionId;
            _entry = entry;
        }

        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);

            if (entry is null)
            {
                return;
            }

            entry.Semaphore.Release();
            Leave(_sessionId, entry);
        }
    }
}
