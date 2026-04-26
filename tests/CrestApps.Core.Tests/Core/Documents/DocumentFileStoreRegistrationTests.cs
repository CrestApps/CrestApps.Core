using System.Text;
using CrestApps.Core.AI.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Documents;

public sealed class DocumentFileStoreRegistrationTests
{
    [Fact]
    public async Task AddCoreAIDocumentProcessing_RegistersDefaultFileSystemStore()
    {
        var contentRoot = CreateTempDirectory();

        try
        {
            var services = new ServiceCollection()
                .AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot))
                .AddLogging()
                .AddCoreAIDocumentProcessing();
            await using var provider = services.BuildServiceProvider();
            var fileStore = provider.GetRequiredService<IDocumentFileStore>();

            Assert.IsType<FileSystemFileStore>(fileStore);

            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("test document"));
            var storedPath = await fileStore.SaveFileAsync("documents/chat/session/file.txt", content);
            var expectedPath = Path.Combine(contentRoot, "App_Data", "Documents", "documents", "chat", "session", "file.txt");

            Assert.Equal(expectedPath, storedPath);
            Assert.True(File.Exists(expectedPath));
        }
        finally
        {
            DeleteDirectory(contentRoot);
        }
    }

    [Fact]
    public async Task AddCoreAIDocumentProcessing_UsesConfiguredFileSystemBasePath()
    {
        var contentRoot = CreateTempDirectory();

        try
        {
            var services = new ServiceCollection()
                .AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot))
                .Configure<DocumentFileSystemFileStoreOptions>(options => options.BasePath = "CustomDocuments")
                .AddLogging()
                .AddCoreAIDocumentProcessing();
            await using var provider = services.BuildServiceProvider();
            var fileStore = provider.GetRequiredService<IDocumentFileStore>();

            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("test document"));
            var storedPath = await fileStore.SaveFileAsync("documents/chat/session/file.txt", content);
            var expectedPath = Path.Combine(contentRoot, "CustomDocuments", "documents", "chat", "session", "file.txt");

            Assert.Equal(expectedPath, storedPath);
            Assert.True(File.Exists(expectedPath));
        }
        finally
        {
            DeleteDirectory(contentRoot);
        }
    }

    [Fact]
    public async Task AddCoreAIDocumentProcessing_PostConfigureOverridesDefaultBasePath()
    {
        var contentRoot = CreateTempDirectory();
        var overriddenPath = Path.Combine(contentRoot, "PostConfiguredDocuments");

        try
        {
            var services = new ServiceCollection()
                .AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot))
                .AddSingleton<IPostConfigureOptions<DocumentFileSystemFileStoreOptions>>(
                    new PostConfigureOptions<DocumentFileSystemFileStoreOptions>(Options.DefaultName, options =>
                    {
                        options.BasePath = overriddenPath;
                    }))
                .AddLogging()
                .AddCoreAIDocumentProcessing();
            await using var provider = services.BuildServiceProvider();
            var fileStore = provider.GetRequiredService<IDocumentFileStore>();

            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("test document"));
            var storedPath = await fileStore.SaveFileAsync("documents/chat/session/file.txt", content);
            var expectedPath = Path.Combine(overriddenPath, "documents", "chat", "session", "file.txt");

            Assert.Equal(expectedPath, storedPath);
            Assert.True(File.Exists(expectedPath));
        }
        finally
        {
            DeleteDirectory(contentRoot);
        }
    }

    [Fact]
    public void AddCoreAIDocumentProcessing_DoesNotReplaceCustomDocumentFileStore()
    {
        var customFileStore = new TestDocumentFileStore();
        var services = new ServiceCollection()
            .AddSingleton<IDocumentFileStore>(customFileStore)
            .AddLogging()
            .AddCoreAIDocumentProcessing();
        using var provider = services.BuildServiceProvider();
        var fileStore = provider.GetRequiredService<IDocumentFileStore>();

        Assert.Same(customFileStore, fileStore);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "crestapps-core-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CrestApps.Core.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class TestDocumentFileStore : IDocumentFileStore
    {
        public Task<bool> DeleteFileAsync(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);

return Task.FromResult(true);
        }

        public Task<Stream> GetFileAsync(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);

return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task<string> SaveFileAsync(string fileName, Stream content)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            ArgumentNullException.ThrowIfNull(content);

return Task.FromResult(fileName);
        }
    }
}
