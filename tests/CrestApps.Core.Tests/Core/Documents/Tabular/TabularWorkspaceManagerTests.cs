using CrestApps.Core.AI.Documents.Tabular;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public class TabularWorkspaceManagerTests
{
    private const string Csv = "region,amount\nNorth,100\nSouth,200\nNorth,50";
    private const string ConversationKey = "conv-1";

    [Fact]
    public async Task EnsureReadyAsync_LoadsTableWithSchema()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = CreateManager();

        var tables = await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), Loader(Csv), cancellationToken);

        var table = Assert.Single(tables);
        Assert.Equal("sales", table.TableName);
        Assert.Equal("sales.csv", table.SourceFileName);
        Assert.Equal(3, table.RowCount);
        Assert.Equal(["region", "amount"], table.Columns.Select(c => c.Name));
    }

    [Fact]
    public async Task QueryAsync_RunsAggregation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = CreateManager();
        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), Loader(Csv), cancellationToken);

        var result = await manager.QueryAsync(
            ConversationKey,
            "SELECT region, SUM(CAST(amount AS INTEGER)) AS total FROM sales GROUP BY region ORDER BY region",
            100,
            cancellationToken);

        Assert.Equal(["region", "total"], result.Columns);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(["North", "150"], result.Rows[0]);
        Assert.Equal(["South", "200"], result.Rows[1]);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task QueryAsync_TruncatesToRowLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = CreateManager(options: new TabularWorkspaceOptions { MaxRowsPerQuery = 2 });
        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), Loader(Csv), cancellationToken);

        var result = await manager.QueryAsync(ConversationKey, "SELECT * FROM sales", 100, cancellationToken);

        Assert.Equal(2, result.Rows.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task QueryAsync_RejectsNonSelect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = CreateManager();
        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), Loader(Csv), cancellationToken);

        await Assert.ThrowsAsync<TabularSqlException>(
            () => manager.QueryAsync(ConversationKey, "UPDATE sales SET amount = '1'", 100, cancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_MutatesInMemoryCopy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = CreateManager();
        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), Loader(Csv), cancellationToken);

        var command = await manager.ExecuteAsync(ConversationKey, "UPDATE sales SET amount = '300' WHERE region = 'South'", cancellationToken);
        Assert.Equal(1, command.AffectedRows);

        var result = await manager.QueryAsync(ConversationKey, "SELECT amount FROM sales WHERE region = 'South'", 100, cancellationToken);
        Assert.Equal("300", Assert.Single(result.Rows)[0]);
    }

    [Fact]
    public async Task ExecuteAsync_AddsColumn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = CreateManager();
        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), Loader(Csv), cancellationToken);

        await manager.ExecuteAsync(ConversationKey, "ALTER TABLE sales ADD COLUMN country TEXT", cancellationToken);
        await manager.ExecuteAsync(ConversationKey, "UPDATE sales SET country = 'US'", cancellationToken);

        var tables = await manager.GetTablesAsync(ConversationKey, cancellationToken);
        Assert.Contains(Assert.Single(tables).Columns, c => c.Name == "country");
    }

    [Fact]
    public async Task ExecuteAsync_RejectsForbiddenStatement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = CreateManager();
        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), Loader(Csv), cancellationToken);

        await Assert.ThrowsAsync<TabularSqlException>(
            () => manager.ExecuteAsync(ConversationKey, "ATTACH DATABASE 'x' AS y", cancellationToken));
    }

    [Fact]
    public async Task EnsureReadyAsync_SameRequest_ReusesDatabaseWithoutReloading()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = CreateManager();
        var loadCount = 0;
        var loader = CountingLoader(Csv, () => loadCount++);

        // Multiple tabular tool calls in the same request (same request id) must not rebuild
        // the table or reload the file content.
        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), loader, cancellationToken);
        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), loader, cancellationToken);
        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), loader, cancellationToken);

        Assert.Equal(1, loadCount);
    }

    [Fact]
    public async Task ReleaseRequest_DisposesDatabase_WhenNoActiveRequests()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = CreateManager();
        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), Loader(Csv), cancellationToken);

        manager.ReleaseRequest(ConversationKey, "req-1");

        // The in-memory database is disposed once the prompt completes.
        var tables = await manager.GetTablesAsync(ConversationKey, cancellationToken);
        Assert.Empty(tables);

        await Assert.ThrowsAsync<TabularSqlException>(
            () => manager.QueryAsync(ConversationKey, "SELECT * FROM sales", 100, cancellationToken));
    }

    [Fact]
    public async Task ReleaseRequest_KeepsDatabase_WhileAnotherRequestActive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = CreateManager();

        // Two concurrent requests for the same conversation share the live database.
        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), Loader(Csv), cancellationToken);
        await manager.EnsureReadyAsync(ConversationKey, "req-2", Documents(), Loader(Csv), cancellationToken);

        manager.ReleaseRequest(ConversationKey, "req-1");

        // Still alive because req-2 is using it.
        var stillLoaded = await manager.GetTablesAsync(ConversationKey, cancellationToken);
        Assert.Single(stillLoaded);

        manager.ReleaseRequest(ConversationKey, "req-2");

        var disposed = await manager.GetTablesAsync(ConversationKey, cancellationToken);
        Assert.Empty(disposed);
    }

    [Fact]
    public async Task NewRequest_AfterRelease_RebuildsFreshDatabaseAndReplaysManipulations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = CreateManager();
        var loadCount = 0;
        var loader = CountingLoader(Csv, () => loadCount++);

        // First prompt: load, manipulate, then complete (release).
        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), loader, cancellationToken);
        await manager.ExecuteAsync(ConversationKey, "UPDATE sales SET amount = '999' WHERE region = 'North'", cancellationToken);
        manager.ReleaseRequest(ConversationKey, "req-1");

        Assert.Equal(1, loadCount);

        // Second prompt asks about the same file again: a brand-new in-memory database is built
        // (file reloaded) and the previous manipulation is replayed so the latest state is preserved.
        await manager.EnsureReadyAsync(ConversationKey, "req-2", Documents(), loader, cancellationToken);

        Assert.Equal(2, loadCount);

        var result = await manager.QueryAsync(ConversationKey, "SELECT DISTINCT amount FROM sales WHERE region = 'North'", 100, cancellationToken);
        Assert.Equal("999", Assert.Single(result.Rows)[0]);
    }

    [Fact]
    public async Task RemoveConversation_DropsDatabaseAndJournal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var manager = CreateManager();
        var loadCount = 0;
        var loader = CountingLoader(Csv, () => loadCount++);

        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), loader, cancellationToken);
        await manager.ExecuteAsync(ConversationKey, "UPDATE sales SET amount = '999' WHERE region = 'North'", cancellationToken);

        manager.RemoveConversation(ConversationKey);

        // Rebuilding produces a clean table with no replayed manipulation (journal was dropped).
        await manager.EnsureReadyAsync(ConversationKey, "req-2", Documents(), loader, cancellationToken);

        var result = await manager.QueryAsync(ConversationKey, "SELECT amount FROM sales WHERE region = 'North' ORDER BY amount", 100, cancellationToken);
        Assert.Equal(["100", "50"], result.Rows.Select(r => r[0]).OrderBy(v => v));
    }

    [Fact]
    public async Task IdleBackstop_DisposesLeakedDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var options = new TabularWorkspaceOptions
        {
            IdleTimeout = TimeSpan.FromMinutes(10),
            JournalRetention = TimeSpan.FromHours(2),
            SweepInterval = TimeSpan.FromMinutes(1),
        };

        using var manager = CreateManager(time, options);

        // Build a workspace but never release it (simulating a leaked request).
        await manager.EnsureReadyAsync(ConversationKey, "req-1", Documents(), Loader(Csv), cancellationToken);

        // Advance past the idle timeout so the sweep disposes the heavy database as a backstop.
        time.Advance(TimeSpan.FromMinutes(11));

        var afterEviction = await manager.GetTablesAsync(ConversationKey, cancellationToken);
        Assert.Empty(afterEviction);
    }

    private static TabularWorkspaceManager CreateManager(TimeProvider timeProvider = null, TabularWorkspaceOptions options = null)
    {
        return new TabularWorkspaceManager(
            Options.Create(options ?? new TabularWorkspaceOptions()),
            timeProvider ?? TimeProvider.System,
            NullLogger<TabularWorkspaceManager>.Instance);
    }

    private static IReadOnlyList<TabularDocumentRef> Documents()
    {
        return [new TabularDocumentRef("doc1", "sales.csv")];
    }

    private static Func<string, CancellationToken, Task<string>> Loader(string content)
    {
        return (_, _) => Task.FromResult(content);
    }

    private static Func<string, CancellationToken, Task<string>> CountingLoader(string content, Action onLoad)
    {
        return (_, _) =>
        {
            onLoad();

            return Task.FromResult(content);
        };
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now;

        public ManualTimeProvider(DateTimeOffset start)
        {
            _now = start;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan delta)
        {
            _now += delta;

            foreach (var timer in _timers.ToArray())
            {
                timer.MaybeFire(_now);
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, _now, dueTime, period);
            _timers.Add(timer);

            return timer;
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly TimerCallback _callback;
            private readonly object _state;
            private readonly TimeSpan _period;
            private DateTimeOffset _nextDue;
            private bool _disposed;

            public ManualTimer(TimerCallback callback, object state, DateTimeOffset now, TimeSpan dueTime, TimeSpan period)
            {
                _callback = callback;
                _state = state;
                _period = period;
                _nextDue = now + dueTime;
            }

            public void MaybeFire(DateTimeOffset now)
            {
                if (_disposed)
                {
                    return;
                }

                while (now >= _nextDue)
                {
                    _callback(_state);

                    if (_period <= TimeSpan.Zero)
                    {
                        _nextDue = DateTimeOffset.MaxValue;
                        break;
                    }

                    _nextDue += _period;
                }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                return true;
            }

            public void Dispose()
            {
                _disposed = true;
            }

            public ValueTask DisposeAsync()
            {
                _disposed = true;

                return ValueTask.CompletedTask;
            }
        }
    }
}
