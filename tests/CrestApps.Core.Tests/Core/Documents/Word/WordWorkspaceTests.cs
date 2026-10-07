using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word;
using CrestApps.Core.AI.Documents.Word.Services;
using CrestApps.Core.AI.Documents.Word.Tools;
using CrestApps.Core.AI.Documents.Word.Workspace;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Templates.Models;
using CrestApps.Core.Templates.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Documents.Word;

public sealed partial class WordWorkspaceTests
{
    [Fact]
    public async Task EditWithSaveAs_NameOfAnotherDocument_KeepsThatDocumentAndSavesUnderNewName()
    {
        using var host = new WordToolTestHost();

        await CreateAsync(host, "draft", "Draft text.");
        await CreateAsync(host, "final", "Final text.");

        var before = await host.LoadWorkspaceAsync();
        var finalBefore = before.Find("final");
        var finalBytes = await host.ReadWorkingDocumentAsync("final");

        var answer = await host.InvokeAsync(new AddWordContentTool(), new
        {
            document = "draft",
            save_as = "final",
            content = new object[] { new { type = "paragraph", text = "Added to the draft." } },
        });

        var after = await host.LoadWorkspaceAsync();
        var finalAfter = after.Find("final");

        Assert.Contains("final-2", answer, StringComparison.Ordinal);
        Assert.Equal(finalBefore.Version, finalAfter.Version);
        Assert.Equal(finalBefore.BlobPath, finalAfter.BlobPath);
        Assert.Equal(finalBytes, await host.ReadWorkingDocumentAsync("final"));
        Assert.NotNull(after.Find("final-2"));
        Assert.Equal(1, after.Find("draft").Version);
    }

    [Fact]
    public async Task EditWithSaveAs_OwnName_ReplacesTheDocument()
    {
        using var host = new WordToolTestHost();

        await CreateAsync(host, "draft", "Draft text.");

        await host.InvokeAsync(new AddWordContentTool(), new
        {
            document = "draft",
            save_as = "draft",
            content = new object[] { new { type = "paragraph", text = "More." } },
        });

        var state = await host.LoadWorkspaceAsync();

        Assert.Single(state.Documents);
        Assert.Equal(2, state.Find("draft").Version);
    }

    [Fact]
    public async Task Edit_StateSaveFails_KeepsThePreviousVersionReadable()
    {
        FailingWorkspaceStore store = null;

        using var host = new WordToolTestHost(configure: services => services.AddSingleton<IWordWorkspaceStore>(provider =>
            store = new FailingWorkspaceStore(ActivatorUtilities.CreateInstance<DocumentFileStoreWordWorkspaceStore>(provider))));

        await CreateAsync(host, "draft", "Draft text.");

        var before = (await host.LoadWorkspaceAsync()).Find("draft");

        store.FailSave = true;

        var answer = await host.InvokeAsync(new AddWordContentTool(), new
        {
            document = "draft",
            content = new object[] { new { type = "paragraph", text = "Lost." } },
        });

        store.FailSave = false;

        var after = (await host.LoadWorkspaceAsync()).Find("draft");

        Assert.Contains("failed", answer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before.BlobPath, after.BlobPath);
        Assert.Equal(before.Version, after.Version);
        Assert.NotNull(await store.ReadBlobAsync(after.BlobPath, TestContext.Current.CancellationToken));

        // The document still opens and edits.
        var next = await host.InvokeAsync(new AddWordContentTool(), new
        {
            document = "draft",
            content = new object[] { new { type = "paragraph", text = "Kept." } },
        });

        Assert.DoesNotContain("failed", next, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, (await host.LoadWorkspaceAsync()).Find("draft").Version);
    }

    [Fact]
    public async Task Edit_Succeeds_DeletesThePreviousVersionsFile()
    {
        using var host = new WordToolTestHost();

        await CreateAsync(host, "draft", "Draft text.");

        var before = (await host.LoadWorkspaceAsync()).Find("draft");

        await host.InvokeAsync(new AddWordContentTool(), new
        {
            document = "draft",
            content = new object[] { new { type = "paragraph", text = "More." } },
        });

        var store = host.Services.GetRequiredService<IWordWorkspaceStore>();

        Assert.Null(await store.ReadBlobAsync(before.BlobPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadBytes_VersionReplacedByAnotherCall_ReadsTheCurrentVersion()
    {
        using var host = new WordToolTestHost();

        await CreateAsync(host, "draft", "Draft text.");

        // A reading call finds the document, then an edit running alongside it replaces that version.
        var reader = await WordToolContext.ResolveAsync(host.Services, TestContext.Current.CancellationToken);
        var source = await reader.FindDocumentAsync("draft", TestContext.Current.CancellationToken);

        await host.InvokeAsync(new AddWordContentTool(), new
        {
            document = "draft",
            content = new object[] { new { type = "paragraph", text = "Edited alongside." } },
        });

        var bytes = await reader.ReadBytesAsync(source, TestContext.Current.CancellationToken);

        using var package = WordPackage.Open(bytes);

        Assert.Contains("Edited alongside.", package.Body.InnerText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenUpload_DecompressionBomb_IsRefused()
    {
        using var host = new WordToolTestHost();

        // 20 MB of zeros compresses to about 20 KB.
        await host.UploadAsync("bomb.docx", AddPart(NewDocument(), "word/media/padding.bin", new byte[20 * 1024 * 1024]));

        var answer = await host.InvokeAsync(new GetWordDocumentTool(), new { document = "bomb.docx" });

        Assert.Contains("\"bomb.docx\" is not opened", answer, StringComparison.Ordinal);
        Assert.Contains("far more than a Word document's content compresses", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenUpload_UnpacksBeyondTheLimit_IsRefused()
    {
        using var host = new WordToolTestHost(configure: services => services.Configure<WordAgentOptions>(options => options.MaxUncompressedDocumentBytes = 1024 * 1024));

        await host.UploadAsync("large.docx", AddPart(NewDocument(), "word/media/padding.bin", Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("lorem ipsum ", 200_000)))));

        var answer = await host.InvokeAsync(new GetWordDocumentTool(), new { document = "large.docx" });

        Assert.Contains("\"large.docx\" expands to more than 1,048,576 bytes once unpacked", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void ZipReader_PartDeclaresLessThanItHolds_StopsAtTheDeclaredSize()
    {
        // The upload check trusts the sizes an archive declares, because the reader the Open XML SDK opens parts
        // through never unpacks more than that. This pins that behavior.
        var bytes = AddPart(NewDocument(), "word/media/padding.bin", Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("lorem ipsum ", 200_000))));

        DeclareUncompressedSize(bytes, "word/media/padding.bin", 16);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var part = archive.GetEntry("word/media/padding.bin").Open();
        using var unpacked = new MemoryStream();

        try
        {
            part.CopyTo(unpacked);
        }
        catch (InvalidDataException)
        {
            // A reader that refuses the part outright is just as safe.
        }

        Assert.True(unpacked.Length <= 16, $"{unpacked.Length:N0} bytes were unpacked.");
    }

    [Fact]
    public async Task OpenUpload_OrdinaryDocument_Opens()
    {
        using var host = new WordToolTestHost();

        await host.UploadAsync("plain.docx", NewDocument());

        var answer = await host.InvokeAsync(new GetWordDocumentTool(), new { document = "plain.docx" });

        Assert.DoesNotContain("not opened", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("unpacked", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateDocument_AtMaxWorkingDocuments_IsRefused()
    {
        using var host = new WordToolTestHost(configure: services => services.Configure<WordAgentOptions>(options => options.MaxWorkingDocuments = 2));

        await CreateAsync(host, "one", "One.");
        await CreateAsync(host, "two", "Two.");

        var create = await host.InvokeAsync(new CreateWordDocumentTool(), new { name = "three" });
        var saveAs = await host.InvokeAsync(new AddWordContentTool(), new
        {
            document = "one",
            save_as = "copy",
            content = new object[] { new { type = "paragraph", text = "More." } },
        });

        Assert.Contains("already holds 2 working documents", create, StringComparison.Ordinal);
        Assert.Contains("already holds 2 working documents", saveAs, StringComparison.Ordinal);
        Assert.Equal(2, (await host.LoadWorkspaceAsync()).Documents.Count);
    }

    [Fact]
    public async Task OrchestrationHandler_AgentDisabled_AddsNothing()
    {
        var interaction = new ChatInteraction
        {
            ItemId = "interaction-1",
            Documents = [new ChatDocumentInfo { DocumentId = "doc-1", FileName = "report.docx" }],
        };

        var enabled = new OrchestrationContext { CompletionContext = new AICompletionContext() };
        var disabled = new OrchestrationContext { CompletionContext = new AICompletionContext() };
        var templates = new FixedTemplateService("Delegate Word work to the Word agent.");

        await new WordDocumentOrchestrationHandler(templates, Options.Create(new WordAgentOptions()))
            .BuiltAsync(new OrchestrationContextBuiltContext(interaction, enabled), TestContext.Current.CancellationToken);
        await new WordDocumentOrchestrationHandler(templates, Options.Create(new WordAgentOptions { Enabled = false }))
            .BuiltAsync(new OrchestrationContextBuiltContext(interaction, disabled), TestContext.Current.CancellationToken);

        Assert.Contains("Word agent", enabled.SystemMessageBuilder.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, disabled.SystemMessageBuilder.Length);
    }

    [Fact]
    public async Task HistoryCleared_StoreThrows_DoesNotThrow()
    {
        var handler = new WordWorkspaceHistoryClearedHandler(
            new ThrowingWorkspaceStore(new IOException("The share is offline.")),
            [],
            NullLogger<WordWorkspaceHistoryClearedHandler>.Instance);

        await handler.HistoryClearedAsync(new ChatInteraction { ItemId = "interaction-1" }, [], TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HistoryCleared_Cancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        var handler = new WordWorkspaceHistoryClearedHandler(
            new ThrowingWorkspaceStore(new OperationCanceledException(cancellation.Token)),
            [],
            NullLogger<WordWorkspaceHistoryClearedHandler>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.HistoryClearedAsync(new ChatInteraction { ItemId = "interaction-1" }, [], cancellation.Token));
    }

    [Fact]
    public async Task HistoryCleared_AfterPreview_DeletesThePreviewPictures()
    {
        using var host = new WordToolTestHost();

        await CreateAsync(host, "memo", "Hello.");

        var answer = await host.InvokeAsync(new PreviewWordTool(), new { document = "memo" });
        var shown = MarkerPattern().Matches(answer)
            .Select(match => host.Invocation.ToolReferences[match.Value].ReferenceId)
            .Distinct()
            .ToList();

        Assert.NotEmpty(shown);
        Assert.Equal(shown, (await host.LoadWorkspaceAsync()).PreviewDocumentIds);

        var cleanup = new RecordingCleanupService();
        var handler = new WordWorkspaceHistoryClearedHandler(
            host.Services.GetRequiredService<IWordWorkspaceStore>(),
            [cleanup],
            NullLogger<WordWorkspaceHistoryClearedHandler>.Instance);

        await handler.HistoryClearedAsync(host.Interaction, [], TestContext.Current.CancellationToken);

        Assert.Equal(shown, cleanup.Deleted);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }

    [Fact]
    public async Task DeleteWorkspace_FileCannotBeDeleted_KeepsItListedForTheNextDelete()
    {
        var root = Path.Combine(Path.GetTempPath(), "word-tool-tests", Guid.NewGuid().ToString("N"));
        var files = new FlakyFileStore(new FileSystemFileStore(root));
        var store = new DocumentFileStoreWordWorkspaceStore(files, NullLogger<DocumentFileStoreWordWorkspaceStore>.Instance);
        var scope = new WordWorkspaceScope("interaction-" + Guid.NewGuid().ToString("N"), AIReferenceTypes.Document.ChatInteraction);

        try
        {
            var path = await store.WriteBlobAsync(scope, NewDocument(), ".docx", TestContext.Current.CancellationToken);

            await store.SaveAsync(scope, new WordWorkspaceState { Documents = [new WordWorkingDocument { Name = "doc", BlobPath = path }] }, TestContext.Current.CancellationToken);

            files.FailDeleteOf = path;
            await store.DeleteAsync(scope, TestContext.Current.CancellationToken);

            var left = await store.LoadAsync(scope, TestContext.Current.CancellationToken);

            Assert.Empty(left.Documents);
            Assert.Equal([path], left.OrphanedBlobs);

            files.FailDeleteOf = null;
            await store.DeleteAsync(scope, TestContext.Current.CancellationToken);

            Assert.Null(await store.ReadBlobAsync(path, TestContext.Current.CancellationToken));
            Assert.Null(await files.GetFileAsync(scope.StatePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LoadWorkspace_Unreadable_RemembersTheFilesItNamesForDeletion()
    {
        var root = Path.Combine(Path.GetTempPath(), "word-tool-tests", Guid.NewGuid().ToString("N"));
        var files = new FileSystemFileStore(root);
        var store = new DocumentFileStoreWordWorkspaceStore(files, NullLogger<DocumentFileStoreWordWorkspaceStore>.Instance);
        var scope = new WordWorkspaceScope("interaction-" + Guid.NewGuid().ToString("N"), AIReferenceTypes.Document.ChatInteraction);

        try
        {
            var path = await store.WriteBlobAsync(scope, NewDocument(), ".docx", TestContext.Current.CancellationToken);

            using (var buffer = new MemoryStream())
            {
                await using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
                {
                    await gzip.WriteAsync(Encoding.UTF8.GetBytes("{\"documents\":[{\"name\":\"doc\",\"blob_path\":\"" + path + "\""), TestContext.Current.CancellationToken);
                }

                buffer.Position = 0;
                await files.SaveFileAsync(scope.StatePath, buffer);
            }

            var state = await store.LoadAsync(scope, TestContext.Current.CancellationToken);

            Assert.Empty(state.Documents);
            Assert.Equal([path], state.OrphanedBlobs);

            await store.DeleteAsync(scope, TestContext.Current.CancellationToken);

            Assert.Null(await store.ReadBlobAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Lock_ReleasedByEveryHolderAndWaiter_IsForgotten()
    {
        var store = new DocumentFileStoreWordWorkspaceStore(new FileSystemFileStore(Path.GetTempPath()), NullLogger<DocumentFileStoreWordWorkspaceStore>.Instance);
        var scope = new WordWorkspaceScope("lock-" + Guid.NewGuid().ToString("N"), AIReferenceTypes.Document.ChatInteraction);

        var first = await store.LockAsync(scope, TestContext.Current.CancellationToken);
        var second = store.LockAsync(scope, TestContext.Current.CancellationToken);

        using (var cancellation = new CancellationTokenSource())
        {
            var cancelled = store.LockAsync(scope, cancellation.Token);

            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        }

        Assert.False(second.IsCompleted);
        Assert.True(DocumentFileStoreWordWorkspaceStore.IsLockTracked(scope));

        first.Dispose();
        (await second).Dispose();

        Assert.False(DocumentFileStoreWordWorkspaceStore.IsLockTracked(scope));
    }

    private static async Task CreateAsync(WordToolTestHost host, string name, string text)
    {
        var answer = await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name,
            content = new object[] { new { type = "paragraph", text } },
        });

        Assert.Contains($"Created working document \"{name}\"", answer, StringComparison.Ordinal);
    }

    private static byte[] NewDocument()
    {
        using var package = WordPackage.Create(new WordDesign());

        return package.Save();
    }

    private static byte[] AddPart(byte[] document, string name, byte[] content)
    {
        using var buffer = new MemoryStream();

        buffer.Write(document);

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();

            stream.Write(content);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Rewrites the uncompressed size a zip archive declares for a part, in its local and central headers.
    /// </summary>
    private static void DeclareUncompressedSize(byte[] archive, string name, int size)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        var patched = 0;

        for (var offset = 0; offset < archive.Length - 46; offset++)
        {
            if (archive[offset] != 0x50 || archive[offset + 1] != 0x4B)
            {
                continue;
            }

            // Local file header: the size at 22, the name length at 26, the name at 30.
            if (archive[offset + 2] == 0x03 && archive[offset + 3] == 0x04 && NameAt(archive, offset + 30, BitConverter.ToUInt16(archive, offset + 26), nameBytes))
            {
                BitConverter.TryWriteBytes(archive.AsSpan(offset + 22, 4), size);
                patched++;
            }

            // Central directory header: the size at 24, the name length at 28, the name at 46.
            if (archive[offset + 2] == 0x01 && archive[offset + 3] == 0x02 && NameAt(archive, offset + 46, BitConverter.ToUInt16(archive, offset + 28), nameBytes))
            {
                BitConverter.TryWriteBytes(archive.AsSpan(offset + 24, 4), size);
                patched++;
            }
        }

        Assert.Equal(2, patched);
    }

    private static bool NameAt(byte[] archive, int offset, int length, byte[] name)
    {
        return length == name.Length &&
            offset + length <= archive.Length &&
            archive.AsSpan(offset, length).SequenceEqual(name);
    }

    [GeneratedRegex(@"\[fig:\d+\]")]
    private static partial Regex MarkerPattern();

    private sealed class FailingWorkspaceStore : IWordWorkspaceStore
    {
        private readonly IWordWorkspaceStore _inner;

        public FailingWorkspaceStore(IWordWorkspaceStore inner)
        {
            _inner = inner;
        }

        public bool FailSave { get; set; }

        public Task<WordWorkspaceState> LoadAsync(WordWorkspaceScope scope, CancellationToken cancellationToken = default) => _inner.LoadAsync(scope, cancellationToken);

        public Task SaveAsync(WordWorkspaceScope scope, WordWorkspaceState state, CancellationToken cancellationToken = default)
        {
            return FailSave
                ? throw new IOException("The workspace could not be saved.")
                : _inner.SaveAsync(scope, state, cancellationToken);
        }

        public Task<byte[]> ReadBlobAsync(string path, CancellationToken cancellationToken = default) => _inner.ReadBlobAsync(path, cancellationToken);

        public Task<string> WriteBlobAsync(WordWorkspaceScope scope, byte[] bytes, string extension, CancellationToken cancellationToken = default) => _inner.WriteBlobAsync(scope, bytes, extension, cancellationToken);

        public Task<bool> DeleteBlobAsync(string path) => _inner.DeleteBlobAsync(path);

        public Task<WordWorkspaceState> DeleteAsync(WordWorkspaceScope scope, CancellationToken cancellationToken = default) => _inner.DeleteAsync(scope, cancellationToken);

        public Task<IDisposable> LockAsync(WordWorkspaceScope scope, CancellationToken cancellationToken = default) => _inner.LockAsync(scope, cancellationToken);
    }

    private sealed class ThrowingWorkspaceStore : IWordWorkspaceStore
    {
        private readonly Exception _exception;

        public ThrowingWorkspaceStore(Exception exception)
        {
            _exception = exception;
        }

        public Task<WordWorkspaceState> LoadAsync(WordWorkspaceScope scope, CancellationToken cancellationToken = default) => Task.FromException<WordWorkspaceState>(_exception);

        public Task SaveAsync(WordWorkspaceScope scope, WordWorkspaceState state, CancellationToken cancellationToken = default) => Task.FromException(_exception);

        public Task<byte[]> ReadBlobAsync(string path, CancellationToken cancellationToken = default) => Task.FromException<byte[]>(_exception);

        public Task<string> WriteBlobAsync(WordWorkspaceScope scope, byte[] bytes, string extension, CancellationToken cancellationToken = default) => Task.FromException<string>(_exception);

        public Task<bool> DeleteBlobAsync(string path) => Task.FromException<bool>(_exception);

        public Task<WordWorkspaceState> DeleteAsync(WordWorkspaceScope scope, CancellationToken cancellationToken = default) => Task.FromException<WordWorkspaceState>(_exception);

        public Task<IDisposable> LockAsync(WordWorkspaceScope scope, CancellationToken cancellationToken = default) => Task.FromException<IDisposable>(_exception);
    }

    private sealed class FlakyFileStore : IDocumentFileStore
    {
        private readonly IDocumentFileStore _inner;

        public FlakyFileStore(IDocumentFileStore inner)
        {
            _inner = inner;
        }

        public string FailDeleteOf { get; set; }

        public Task<string> SaveFileAsync(string fileName, Stream content) => _inner.SaveFileAsync(fileName, content);

        public Task<Stream> GetFileAsync(string fileName) => _inner.GetFileAsync(fileName);

        public Task<bool> DeleteFileAsync(string fileName)
        {
            return string.Equals(fileName, FailDeleteOf, StringComparison.Ordinal)
                ? throw new IOException("The file is locked.")
                : _inner.DeleteFileAsync(fileName);
        }
    }

    private sealed class RecordingCleanupService : IConversationDocumentCleanupService
    {
        public List<string> Deleted { get; } = [];

        public Task CleanupAsync(string referenceId, string referenceType, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CleanupGeneratedDocumentsAsync(IEnumerable<string> documentIds, CancellationToken cancellationToken = default)
        {
            Deleted.AddRange(documentIds);

            return Task.CompletedTask;
        }
    }

    private sealed class FixedTemplateService : ITemplateService
    {
        private readonly string _text;

        public FixedTemplateService(string text)
        {
            _text = text;
        }

        public Task<string> RenderAsync(string id, IDictionary<string, object> arguments = null, CancellationToken cancellationToken = default) => Task.FromResult(_text);

        public Task<IReadOnlyList<Template>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Template>>([]);

        public Task<Template> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Template>(null);

        public Task<string> MergeAsync(IEnumerable<string> ids, IDictionary<string, object> arguments = null, string separator = "\n\n", CancellationToken cancellationToken = default) => Task.FromResult(_text);
    }
}
