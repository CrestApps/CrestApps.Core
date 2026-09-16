using System.Text;
using CrestApps.Core.AI.Indexers.FileTransfer;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Indexers;

/// <summary>
/// Covers the rules a file-server connector has to get right, none of which are about the protocol: what
/// counts as changed, what a failed listing must not cause, and what an item identifier may reach.
/// </summary>
public sealed class RemoteFileConnectorTests
{
    /// <summary>
    /// Verifies that a listed file carries a token built from what the server reported, and that the token
    /// changes when the file does.
    /// </summary>
    [Fact]
    public async Task Discover_ListedFile_CarriesAChangeToken()
    {
        var client = new FakeRemoteFileClient
        {
            Listing = new RemoteListing(
            [
                new RemoteFile("reports/first.pdf", 1024, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
            ],
            IsComplete: true),
        };

        var connector = new TestRemoteConnector(client);

        var first = await connector.DiscoverAsync(CreateIndexer(), cancellationToken: TestContext.Current.CancellationToken);
        var item = Assert.Single(first.Items);

        Assert.Equal("reports/first.pdf", item.ItemId);
        Assert.False(string.IsNullOrWhiteSpace(item.ChangeToken));

        client.Listing = new RemoteListing(
        [
            new RemoteFile("reports/first.pdf", 2048, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
        ],
        IsComplete: true);

        var second = await connector.DiscoverAsync(CreateIndexer(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(item.ChangeToken, Assert.Single(second.Items).ChangeToken);
    }

    /// <summary>
    /// Verifies that a file the server describes with neither a size nor a time gets no token, so it is
    /// re-read rather than assumed unchanged. FTP in particular often reports neither.
    /// </summary>
    [Fact]
    public async Task Discover_FileWithNoMetadata_HasNoChangeToken()
    {
        var connector = new TestRemoteConnector(new FakeRemoteFileClient
        {
            Listing = new RemoteListing([new RemoteFile("notes.txt")], IsComplete: true),
        });

        var result = await connector.DiscoverAsync(CreateIndexer(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(result.Items).ChangeToken);
    }

    /// <summary>
    /// Verifies that a listing the server could not complete is reported as incomplete, which is what stops
    /// everything it failed to report being deleted.
    /// </summary>
    [Fact]
    public async Task Discover_PartialListing_IsReportedIncomplete()
    {
        var connector = new TestRemoteConnector(new FakeRemoteFileClient
        {
            Listing = new RemoteListing(
                [new RemoteFile("first.txt", 10)],
                IsComplete: false,
                "The connection dropped."),
        });

        var result = await connector.DiscoverAsync(CreateIndexer(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Single(result.Items);
    }

    /// <summary>
    /// Verifies that a listing that threw is an empty, incomplete result rather than an exception that ends
    /// the run and rather than an empty complete one that deletes everything.
    /// </summary>
    [Fact]
    public async Task Discover_ListingThrows_IsEmptyAndIncomplete()
    {
        var connector = new TestRemoteConnector(new FakeRemoteFileClient
        {
            Throw = new IOException("The connection was reset."),
        });

        var result = await connector.DiscoverAsync(CreateIndexer(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Items);
    }

    /// <summary>
    /// Verifies that a file is fetched with the media type its name implies, so the right reader gets it.
    /// </summary>
    [Fact]
    public async Task Fetch_ReturnsContentWithInferredMediaType()
    {
        var client = new FakeRemoteFileClient
        {
            Files = { ["reports/first.pdf"] = "%PDF-1.7"u8.ToArray() },
        };

        var connector = new TestRemoteConnector(client);

        await using var content = await connector.FetchAsync(CreateIndexer(), "reports/first.pdf", TestContext.Current.CancellationToken);

        Assert.NotNull(content);
        Assert.Equal("application/pdf", content.MediaType);
        Assert.Equal("first.pdf", content.FileName);
    }

    /// <summary>
    /// Verifies that an item identifier that climbs out of the indexed folder reads nothing, whatever it was
    /// stored as.
    /// </summary>
    [Fact]
    public async Task Fetch_ItemIdClimbingOutOfRoot_ReturnsNothing()
    {
        var client = new FakeRemoteFileClient();
        var connector = new TestRemoteConnector(client);

        var content = await connector.FetchAsync(CreateIndexer(), "../../etc/passwd", TestContext.Current.CancellationToken);

        Assert.Null(content);
        Assert.Equal(0, client.OpenCount);
    }

    /// <summary>
    /// Verifies that reading a file releases the connection it was read over.
    /// </summary>
    [Fact]
    public async Task Fetch_DisposingContent_ReleasesTheConnection()
    {
        var client = new FakeRemoteFileClient
        {
            Files = { ["notes.txt"] = "The notes."u8.ToArray() },
        };

        var connector = new TestRemoteConnector(client);

        var content = await connector.FetchAsync(CreateIndexer(), "notes.txt", TestContext.Current.CancellationToken);

        Assert.False(client.Disposed);

        await content.DisposeAsync();

        Assert.True(client.Disposed);
    }

    private static WebCrawler CreateIndexer()
    {
        var indexer = new WebCrawler
        {
            ItemId = "indexer-1",
            Source = "TestRemote",
            DisplayText = "The server",
            AIDataSourceId = "data-source-1",
            Enabled = true,
        };

        indexer.Put(new RemoteFolderIndexerMetadata
        {
            RootPath = "/reports",
        });

        return indexer;
    }

    /// <summary>
    /// The connector under test, over a fake server.
    /// </summary>
    private sealed class TestRemoteConnector : RemoteFileIngestionConnector
    {
        public TestRemoteConnector(FakeRemoteFileClient client)
            : base(new FakeFactory(client), NullLogger<TestRemoteConnector>.Instance)
        {
        }

        public override ValueTask ValidateAsync(WebCrawler settings, ValidationResultDetails result, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        private sealed class FakeFactory : IRemoteFileClientFactory
        {
            private readonly FakeRemoteFileClient _client;

            public FakeFactory(FakeRemoteFileClient client)
            {
                _client = client;
            }

            public string ConnectorName => "TestRemote";

            public Task<IRemoteFileClient> CreateAsync(WebCrawler indexer, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IRemoteFileClient>(_client);
            }
        }
    }

    /// <summary>
    /// A file server that lives in memory.
    /// </summary>
    private sealed class FakeRemoteFileClient : IRemoteFileClient
    {
        public RemoteListing Listing { get; set; } = new([], IsComplete: true);

        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        public Exception Throw { get; set; }

        public int OpenCount { get; private set; }

        public bool Disposed { get; private set; }

        public Task<RemoteListing> ListAsync(string rootPath, bool recursive, int maxItems, CancellationToken cancellationToken = default)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            return Task.FromResult(Listing);
        }

        public Task<Stream> OpenReadAsync(string rootPath, string path, CancellationToken cancellationToken = default)
        {
            OpenCount++;

            if (!Files.TryGetValue(path, out var content))
            {
                return Task.FromResult<Stream>(null);
            }

            return Task.FromResult<Stream>(new MemoryStream(content));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;

            return ValueTask.CompletedTask;
        }
    }
}
