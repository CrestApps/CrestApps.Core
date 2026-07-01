using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Tabular;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class TabularDocumentArtifactStoreTests
{
    [Fact]
    public async Task SaveAsync_GetAsync_AndDeleteAsync_RoundTripsArtifact()
    {
        var directory = Path.Combine(Path.GetTempPath(), "crestapps-tabular-artifacts", Guid.NewGuid().ToString("N"));

        try
        {
            var fileStore = new FileSystemFileStore(directory);
            var artifactStore = new DocumentFileStoreTabularDocumentArtifactStore(fileStore);
            var artifact = TabularDocumentArtifact.FromDelimitedContent("region,amount\nNorth,100", "sales.csv");

            await artifactStore.SaveAsync("doc-1", artifact, TestContext.Current.CancellationToken);
            var loaded = await artifactStore.GetAsync("doc-1", TestContext.Current.CancellationToken);

            Assert.NotNull(loaded);
            Assert.Equal(["region", "amount"], loaded.Header);
            var row = Assert.Single(loaded.Rows);
            Assert.Equal(["North", "100"], row);

            await artifactStore.DeleteAsync("doc-1", TestContext.Current.CancellationToken);

            Assert.Null(await artifactStore.GetAsync("doc-1", TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
