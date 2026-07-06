using System.Text;
using CrestApps.Core.AI.Documents;

namespace CrestApps.Core.Tests.Core.Documents;

public sealed class FileSystemFileStoreTests
{
    [Fact]
    public async Task DeleteFileAsync_AllowsDeletingWhileReadStreamIsOpen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "crestapps-filestore", Guid.NewGuid().ToString("N"));

        try
        {
            var store = new FileSystemFileStore(directory);
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

            await store.SaveFileAsync("documents/chat-interaction/interaction-1/generated/test.txt", content);
            await using var stream = await store.GetFileAsync("documents/chat-interaction/interaction-1/generated/test.txt");

            Assert.NotNull(stream);
            Assert.True(await store.DeleteFileAsync("documents/chat-interaction/interaction-1/generated/test.txt"));
            Assert.Null(await store.GetFileAsync("documents/chat-interaction/interaction-1/generated/test.txt"));
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
