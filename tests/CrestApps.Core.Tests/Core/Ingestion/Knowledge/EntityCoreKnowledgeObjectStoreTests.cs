using System.Collections.Concurrent;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.EntityCore;
using CrestApps.Core.Data.EntityCore.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Ingestion.Knowledge;

public sealed class EntityCoreKnowledgeObjectStoreTests
{
    private const string FirstDataSourceId = "data-source-1";
    private const string SecondDataSourceId = "data-source-2";
    private const string PromptVersion = "v1";

    /// <summary>
    /// Verifies that the description cache finds the figure made from the same bytes wherever it was ingested,
    /// ignores everything that is not a figure, and asks the database for the hash rather than reading the
    /// figures back and comparing them here. The lookup runs once per pending figure, so an installation with
    /// a couple of hundred ingested documents is exactly where reading them all stops being affordable.
    /// </summary>
    [Fact]
    public async Task FindFigureByContentHash_MatchingBytes_FiltersInTheDatabaseAcrossDataSources()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var harness = await Harness.CreateAsync();

        var chart = CreateFigure(SecondDataSourceId, "chart:shared", KnowledgeObjectTypes.Chart, "hash-shared", "A chart of the measured values.");

        await harness.SeedAsync(
            [
                CreateText(FirstDataSourceId, "text:shared", "hash-shared"),
                CreateFigure(FirstDataSourceId, "figure:other", KnowledgeObjectTypes.Figure, "hash-other", "A photograph."),
                chart,
            ],
            cancellationToken);

        harness.Commands.Clear();

        var match = await harness.Store.FindFigureByContentHashAsync("hash-shared", PromptVersion, cancellationToken);
        var statements = harness.Commands.Snapshot()
            .Where(statement => statement.Contains("SELECT", StringComparison.Ordinal))
            .ToArray();

        Assert.NotNull(match);
        Assert.Equal(chart.CanonicalId, match.CanonicalId);
        Assert.Equal(SecondDataSourceId, match.Source);

        // Every statement the lookup ran narrowed on the hash, so the database returned the objects made from
        // these bytes instead of every figure in the installation.
        Assert.NotEmpty(statements);
        Assert.All(statements, statement =>
        {
            var where = statement.IndexOf("WHERE", StringComparison.Ordinal);

            Assert.True(where >= 0, $"The lookup ran an unfiltered statement: {statement}");
            Assert.Contains("ContentHash", statement[where..], StringComparison.Ordinal);
        });

        // The hash is denormalized onto the record, which is what lets the query filter on it at all.
        Assert.Equal(
            "hash-shared",
            await harness.DbContext.CatalogRecords
                .AsNoTracking()
                .Where(record => record.ItemId == chart.ItemId)
                .Select(record => record.ContentHash)
                .SingleAsync(cancellationToken));
    }

    /// <summary>
    /// Verifies that a figure still waiting for its description, and one transcribed by a prompt that has
    /// since changed, are both treated as a miss. Reusing either would hide a prompt change behind a stale
    /// description.
    /// </summary>
    [Fact]
    public async Task FindFigureByContentHash_UndescribedOrStalePrompt_ReturnsNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var harness = await Harness.CreateAsync();

        var pending = CreateFigure(FirstDataSourceId, "figure:pending", KnowledgeObjectTypes.Figure, "hash-pending", description: null);
        pending.Status = KnowledgeObjectStatus.PendingDescription;

        await harness.SeedAsync(
            [
                pending,
                CreateFigure(FirstDataSourceId, "figure:described", KnowledgeObjectTypes.Figure, "hash-described", "A bar chart."),
            ],
            cancellationToken);

        Assert.Null(await harness.Store.FindFigureByContentHashAsync("hash-pending", PromptVersion, cancellationToken));
        Assert.Null(await harness.Store.FindFigureByContentHashAsync("hash-described", "v2", cancellationToken));
        Assert.Null(await harness.Store.FindFigureByContentHashAsync("hash-missing", PromptVersion, cancellationToken));

        var match = await harness.Store.FindFigureByContentHashAsync("hash-described", PromptVersion, cancellationToken);

        Assert.NotNull(match);
        Assert.Equal("figure:described", match.CanonicalId);
    }

    /// <summary>
    /// Verifies that the identifier lookups stay inside the data source they were asked about, even when two
    /// data sources ingested the same file and produced the same canonical identifiers.
    /// </summary>
    [Fact]
    public async Task FindByCanonicalIdAndRootId_SameIdentifiersInTwoDataSources_StayScopedToTheirOwn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var harness = await Harness.CreateAsync();

        await harness.SeedAsync(
            [
                CreateFigure(FirstDataSourceId, "figure:shared", KnowledgeObjectTypes.Figure, "hash-1", "A photograph."),
                CreateFigure(SecondDataSourceId, "figure:shared", KnowledgeObjectTypes.Figure, "hash-2", "A photograph."),
            ],
            cancellationToken);

        var first = await harness.Store.FindByCanonicalIdAsync(FirstDataSourceId, "figure:shared", cancellationToken);
        var second = await harness.Store.FindByCanonicalIdAsync(SecondDataSourceId, "figure:shared", cancellationToken);

        Assert.Equal("hash-1", first?.ContentHash);
        Assert.Equal("hash-2", second?.ContentHash);

        var byRoot = Assert.Single(await harness.Store.GetByRootIdAsync(SecondDataSourceId, "document:shared", cancellationToken));

        Assert.Equal(SecondDataSourceId, byRoot.Source);
        Assert.Equal("hash-2", byRoot.ContentHash);

        // The hash lookup is the one exception: the same bytes are the same bytes wherever they were ingested.
        var match = await harness.Store.FindFigureByContentHashAsync("hash-2", PromptVersion, cancellationToken);

        Assert.Equal(SecondDataSourceId, match?.Source);
    }

    private static KnowledgeObject CreateFigure(
        string dataSourceId,
        string canonicalId,
        string objectType,
        string contentHash,
        string description)
    {
        var entry = new KnowledgeObject
        {
            Source = dataSourceId,
            CanonicalId = canonicalId,
            ObjectType = objectType,
            RootId = "document:shared",
            ParentId = "article:shared",
            Title = "Figure 1. The measurements.",
            Content = "Figure 1. The measurements.",
            ContentHash = contentHash,
            MediaType = "image/png",
            StoragePath = $"figures/{canonicalId}.png",
            PageStart = 8,
            PageEnd = 8,
            CreatedUtc = DateTime.UtcNow,
        };

        entry.Put(new FigureDetails
        {
            Caption = "Figure 1. The measurements.",
            Tier = "describe",
            Description = description,
            DescriptionPromptVersion = description is null ? null : PromptVersion,
        });

        return entry;
    }

    private static KnowledgeObject CreateText(string dataSourceId, string canonicalId, string contentHash)
    {
        return new KnowledgeObject
        {
            Source = dataSourceId,
            CanonicalId = canonicalId,
            ObjectType = KnowledgeObjectTypes.Text,
            RootId = "document:shared",
            ParentId = "article:shared",
            Content = "The measurements were taken twice.",
            ContentHash = contentHash,
            CreatedUtc = DateTime.UtcNow,
        };
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly ServiceProvider _services;
        private readonly IServiceScope _scope;
        private readonly IStoreCommitter _committer;

        private Harness(
            string databasePath,
            ServiceProvider services,
            IServiceScope scope,
            CommandCapturingLoggerProvider commands)
        {
            _databasePath = databasePath;
            _services = services;
            _scope = scope;
            _committer = scope.ServiceProvider.GetRequiredService<IStoreCommitter>();
            Commands = commands;
            DbContext = scope.ServiceProvider.GetRequiredService<CrestAppsEntityDbContext>();
            Store = new EntityCoreKnowledgeObjectStore(DbContext);
        }

        public CommandCapturingLoggerProvider Commands { get; }

        public CrestAppsEntityDbContext DbContext { get; }

        public EntityCoreKnowledgeObjectStore Store { get; }

        public static async Task<Harness> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
            var commands = new CommandCapturingLoggerProvider();
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.AddProvider(commands));
            services.AddCoreEntityCoreDataStore(
                options => options.UseSqlite($"Data Source={databasePath}"),
                store => store.TablePrefix = "CA_");

            var provider = services.BuildServiceProvider();
            await provider.InitializeEntityCoreSchemaAsync();

            return new Harness(databasePath, provider, provider.CreateScope(), commands);
        }

        public async Task SeedAsync(IEnumerable<KnowledgeObject> entries, CancellationToken cancellationToken)
        {
            foreach (var entry in entries)
            {
                await Store.CreateAsync(entry, cancellationToken);
            }

            await _committer.CommitAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            _scope.Dispose();
            await _services.DisposeAsync();

            if (File.Exists(_databasePath))
            {
                try
                {
                    File.Delete(_databasePath);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    /// <summary>
    /// Keeps the SQL Entity Framework Core runs, so a test can assert what the database was asked for rather
    /// than only what came back.
    /// </summary>
    private sealed class CommandCapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _statements = new();

        /// <summary>
        /// Forgets every statement captured so far.
        /// </summary>
        public void Clear()
        {
            _statements.Clear();
        }

        /// <summary>
        /// Gets the statements captured so far, in the order they were run.
        /// </summary>
        /// <returns>The statements.</returns>
        public string[] Snapshot()
        {
            return _statements.ToArray();
        }

        /// <summary>
        /// Creates the logger for one category, capturing only what the database command category writes.
        /// </summary>
        /// <param name="categoryName">The category.</param>
        /// <returns>The logger.</returns>
        public ILogger CreateLogger(string categoryName)
        {
            return string.Equals(categoryName, DbLoggerCategory.Database.Command.Name, StringComparison.Ordinal)
                ? new CommandCapturingLogger(_statements)
                : NullLogger.Instance;
        }

        /// <summary>
        /// Disposes the provider. There is nothing to release.
        /// </summary>
        public void Dispose()
        {
        }

        private sealed class CommandCapturingLogger : ILogger
        {
            private readonly ConcurrentQueue<string> _statements;

            public CommandCapturingLogger(ConcurrentQueue<string> statements)
            {
                _statements = statements;
            }

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                _statements.Enqueue(formatter(state, exception));
            }

            private sealed class NullScope : IDisposable
            {
                public static NullScope Instance { get; } = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
