using System.Collections.Concurrent;
using CrestApps.Core.Services;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// The committed entries that <see cref="TestStoreSession"/> instances share, registered as a singleton.
/// </summary>
public sealed class TestStoreDatabase
{
    public ConcurrentQueue<object> Committed { get; } = new();
}

/// <summary>
/// A scoped store session for hub tests. Writes are staged on the scope's instance and reach the
/// <see cref="TestStoreDatabase"/> only on commit, and the instance refuses all work once its scope is
/// disposed, as a YesSql session or a DbContext does after SignalR disposes a cancelled stream's
/// invocation scope.
/// </summary>
public sealed class TestStoreSession : IStoreCommitter, IDisposable
{
    private readonly TestStoreDatabase _database;
    private readonly List<object> _staged = [];
    private bool _disposed;

    public TestStoreSession(TestStoreDatabase database)
    {
        _database = database;
    }

    public void Stage(object entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _staged.Add(entry);
    }

    public IReadOnlyList<T> GetStaged<T>()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _staged.OfType<T>().ToList();
    }

    public ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var entry in _staged)
        {
            _database.Committed.Enqueue(entry);
        }

        _staged.Clear();

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
