using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Handlers;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Documents.Handlers;

/// <summary>
/// Clearing a chat interaction's history must take its workspace database with it, so the next tool
/// call rebuilds from the original uploaded files instead of serving mutations from the cleared
/// conversation — and must never reach a database that belongs to a different interaction.
/// </summary>
public sealed class TabularWorkspaceHistoryClearedHandlerTests : IDisposable
{
    private readonly string _basePath;

    public TabularWorkspaceHistoryClearedHandlerTests()
    {
        _basePath = Path.Combine(Path.GetTempPath(), "tabular-history-cleared-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_basePath);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_basePath, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task HistoryClearedAsync_DeletesTheDatabaseAndItsWriteAheadLogSidecars()
    {
        var paths = CreateDatabaseFiles("interaction-1");

        await CreateHandler().HistoryClearedAsync(
            new ChatInteraction { ItemId = "interaction-1" },
            [],
            TestContext.Current.CancellationToken);

        Assert.All(paths, path => Assert.False(File.Exists(path), $"Expected '{path}' to be deleted."));
    }

    [Fact]
    public async Task HistoryClearedAsync_LeavesOtherInteractionsDatabasesAlone()
    {
        var cleared = CreateDatabaseFiles("interaction-1");
        var untouched = CreateDatabaseFiles("interaction-2");

        await CreateHandler().HistoryClearedAsync(
            new ChatInteraction { ItemId = "interaction-1" },
            [],
            TestContext.Current.CancellationToken);

        Assert.All(cleared, path => Assert.False(File.Exists(path)));
        Assert.All(untouched, path => Assert.True(File.Exists(path), $"Expected '{path}' to survive."));
    }

    [Fact]
    public async Task HistoryClearedAsync_DoesNotReachTheChatSessionDatabaseOfTheSameIdentifier()
    {
        var interactionFiles = CreateDatabaseFiles("shared-id");
        var sessionFiles = CreateDatabaseFiles("shared-id", "chat-session");

        await CreateHandler().HistoryClearedAsync(
            new ChatInteraction { ItemId = "shared-id" },
            [],
            TestContext.Current.CancellationToken);

        Assert.All(interactionFiles, path => Assert.False(File.Exists(path)));
        Assert.All(sessionFiles, path => Assert.True(File.Exists(path), $"Expected '{path}' to survive."));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("interaction-1/../interaction-2")]
    public async Task HistoryClearedAsync_WhenTheInteractionIdIsNotASinglePathSegment_DeletesNothing(string itemId)
    {
        var files = CreateDatabaseFiles("interaction-2");

        await CreateHandler().HistoryClearedAsync(
            new ChatInteraction { ItemId = itemId },
            [],
            TestContext.Current.CancellationToken);

        Assert.All(files, path => Assert.True(File.Exists(path), $"Expected '{path}' to survive."));
    }

    [Fact]
    public async Task HistoryClearedAsync_WhenTheInteractionIsMissing_DoesNothing()
    {
        var files = CreateDatabaseFiles("interaction-1");

        await CreateHandler().HistoryClearedAsync(null, [], TestContext.Current.CancellationToken);

        Assert.All(files, path => Assert.True(File.Exists(path)));
    }

    private string[] CreateDatabaseFiles(string referenceId, string referenceType = "chat-interaction")
    {
        var databasePath = Path.Combine(_basePath, "documents", referenceType, referenceId, "data", "tabular.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath));

        string[] paths = [databasePath, databasePath + "-wal", databasePath + "-shm"];

        foreach (var path in paths)
        {
            File.WriteAllText(path, "content");
        }

        return paths;
    }

    private TabularWorkspaceHistoryClearedHandler CreateHandler()
    {
        return new TabularWorkspaceHistoryClearedHandler(
            Options.Create(new DocumentFileSystemFileStoreOptions { BasePath = _basePath }),
            NullLogger<TabularWorkspaceHistoryClearedHandler>.Instance);
    }
}
