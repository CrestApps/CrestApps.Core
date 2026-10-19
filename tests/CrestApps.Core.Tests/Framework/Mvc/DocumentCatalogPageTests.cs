using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Indexes;
using CrestApps.Core.Data.YesSql.Services;
using CrestApps.Core.Models;
using Microsoft.Data.Sqlite;
using YesSql;
using YesSql.Indexes;
using YesSql.Provider.Sqlite;
using YesSql.Sql;

namespace CrestApps.Core.Tests.Framework.Mvc;

/// <summary>
/// Runs <see cref="DocumentCatalog{T, TIndex}.PageAsync{TQuery}"/> against a real SQLite YesSql store, so the
/// name, display text, and source filters are translated to SQL against the concrete index table.
/// </summary>
public sealed class DocumentCatalogPageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PageAsync_WithNameOnNameAwareIndex_ReturnsMatchingEntries(bool sorted)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await TestDatabase.CreateAsync(cancellationToken);
        await database.SaveAsync(
            [
                new NamedEntry { ItemId = "1", Name = "billing-zeta", Source = "A" },
                new NamedEntry { ItemId = "2", Name = "support", Source = "A" },
                new NamedEntry { ItemId = "3", Name = "billing-alpha", Source = "B" },
            ],
            cancellationToken);

        await using var session = database.Store.CreateSession();
        var catalog = new DocumentCatalog<NamedEntry, NamedEntryIndex>(session);

        var result = await catalog.PageAsync(1, 10, new QueryContext { Name = "billing", Sorted = sorted }, cancellationToken);

        Assert.Equal(2, result.Count);

        var names = result.Entries.Select(entry => entry.Name).ToArray();

        if (sorted)
        {
            Assert.Equal(["billing-alpha", "billing-zeta"], names);
        }
        else
        {
            Assert.Equal(["billing-alpha", "billing-zeta"], names.Order());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PageAsync_WithNameOnDisplayTextAwareIndex_ReturnsMatchingEntries(bool sorted)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await TestDatabase.CreateAsync(cancellationToken);
        await database.SaveAsync(
            [
                new DisplayTextEntry { ItemId = "1", DisplayText = "Billing Zeta" },
                new DisplayTextEntry { ItemId = "2", DisplayText = "Support" },
                new DisplayTextEntry { ItemId = "3", DisplayText = "Billing Alpha" },
            ],
            cancellationToken);

        await using var session = database.Store.CreateSession();
        var catalog = new DocumentCatalog<DisplayTextEntry, DisplayTextEntryIndex>(session);

        var result = await catalog.PageAsync(1, 10, new QueryContext { Name = "Billing", Sorted = sorted }, cancellationToken);

        Assert.Equal(2, result.Count);

        var displayTexts = result.Entries.Select(entry => entry.DisplayText).ToArray();

        if (sorted)
        {
            Assert.Equal(["Billing Alpha", "Billing Zeta"], displayTexts);
        }
        else
        {
            Assert.Equal(["Billing Alpha", "Billing Zeta"], displayTexts.Order());
        }
    }

    [Fact]
    public async Task PageAsync_WithSourceAndName_ReturnsEntriesMatchingBoth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await TestDatabase.CreateAsync(cancellationToken);
        await database.SaveAsync(
            [
                new NamedEntry { ItemId = "1", Name = "billing-zeta", Source = "A" },
                new NamedEntry { ItemId = "2", Name = "support", Source = "A" },
                new NamedEntry { ItemId = "3", Name = "billing-alpha", Source = "B" },
            ],
            cancellationToken);

        await using var session = database.Store.CreateSession();
        var catalog = new DocumentCatalog<NamedEntry, NamedEntryIndex>(session);

        var result = await catalog.PageAsync(1, 10, new QueryContext { Name = "billing", Source = "A" }, cancellationToken);

        Assert.Equal(1, result.Count);
        Assert.Equal("billing-zeta", Assert.Single(result.Entries).Name);
    }

    [Fact]
    public async Task PageAsync_WithSearchAndPaging_CountsAllMatchesAndReturnsRequestedPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await TestDatabase.CreateAsync(cancellationToken);
        await database.SaveAsync(
            [
                new DisplayTextEntry { ItemId = "1", DisplayText = "Group 1" },
                new DisplayTextEntry { ItemId = "2", DisplayText = "Group 2" },
                new DisplayTextEntry { ItemId = "3", DisplayText = "Group 3" },
                new DisplayTextEntry { ItemId = "4", DisplayText = "Other" },
            ],
            cancellationToken);

        await using var session = database.Store.CreateSession();
        var catalog = new DocumentCatalog<DisplayTextEntry, DisplayTextEntryIndex>(session);

        var result = await catalog.PageAsync(2, 2, new QueryContext { Name = "Group", Sorted = true }, cancellationToken);

        Assert.Equal(3, result.Count);
        Assert.Equal("Group 3", Assert.Single(result.Entries).DisplayText);
    }

    internal sealed class NamedEntry : CatalogItem
    {
        public string Name { get; set; }

        public string Source { get; set; }
    }

    internal sealed class NamedEntryIndex : CatalogItemIndex, INameAwareIndex, ISourceAwareIndex
    {
        public string Name { get; set; }

        public string Source { get; set; }
    }

    internal sealed class DisplayTextEntry : CatalogItem
    {
        public string DisplayText { get; set; }
    }

    internal sealed class DisplayTextEntryIndex : CatalogItemIndex, IDisplayTextAwareIndex
    {
        public string DisplayText { get; set; }
    }

    private sealed class NamedEntryIndexProvider : IndexProvider<NamedEntry>
    {
        public override void Describe(DescribeContext<NamedEntry> context)
        {
            context.For<NamedEntryIndex>()
                .Map(entry => new NamedEntryIndex
                {
                    ItemId = entry.ItemId,
                    Name = entry.Name,
                    Source = entry.Source,
                });
        }
    }

    private sealed class DisplayTextEntryIndexProvider : IndexProvider<DisplayTextEntry>
    {
        public override void Describe(DescribeContext<DisplayTextEntry> context)
        {
            context.For<DisplayTextEntryIndex>()
                .Map(entry => new DisplayTextEntryIndex
                {
                    ItemId = entry.ItemId,
                    DisplayText = entry.DisplayText,
                });
        }
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _rootConnection;

        private TestDatabase(SqliteConnection rootConnection, IStore store)
        {
            _rootConnection = rootConnection;
            Store = store;
        }

        public IStore Store { get; }

        public static async Task<TestDatabase> CreateAsync(CancellationToken cancellationToken)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"{nameof(DocumentCatalogPageTests)}-{Guid.NewGuid():N}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
            }.ToString();

            var rootConnection = new SqliteConnection(connectionString);
            await rootConnection.OpenAsync(cancellationToken);

            var store = await StoreFactory.CreateAndInitializeAsync(new Configuration().UseSqLite(connectionString));
            store.RegisterIndexes([new NamedEntryIndexProvider(), new DisplayTextEntryIndexProvider()]);

            await using (var connection = store.Configuration.ConnectionFactory.CreateConnection())
            {
                await connection.OpenAsync(cancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                var schemaBuilder = new SchemaBuilder(store.Configuration, transaction);

                await schemaBuilder.CreateMapIndexTableAsync<NamedEntryIndex>(table => table
                    .Column<string>(nameof(NamedEntryIndex.ItemId))
                    .Column<string>(nameof(NamedEntryIndex.Name))
                    .Column<string>(nameof(NamedEntryIndex.Source)));

                await schemaBuilder.CreateMapIndexTableAsync<DisplayTextEntryIndex>(table => table
                    .Column<string>(nameof(DisplayTextEntryIndex.ItemId))
                    .Column<string>(nameof(DisplayTextEntryIndex.DisplayText)));

                await transaction.CommitAsync(cancellationToken);
            }

            return new TestDatabase(rootConnection, store);
        }

        public async Task SaveAsync<T>(IEnumerable<T> documents, CancellationToken cancellationToken)
        {
            await using var session = Store.CreateSession();

            foreach (var document in documents)
            {
                await session.SaveAsync(document, false, null, cancellationToken);
            }

            await session.SaveChangesAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            Store.Dispose();
            await _rootConnection.DisposeAsync();
        }
    }
}
