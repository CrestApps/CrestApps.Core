using System.Text.Json;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Services;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Pdf.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

/// <summary>
/// Runs PDF tools the way a chat interaction runs them: with a conversation, a file store on disk, a document
/// store, the generated-document service and a download endpoint, so a test can upload a PDF, call tools and
/// read back what they produced.
/// </summary>
internal sealed class PdfToolTestHost : IDisposable
{
    private readonly AIInvocationScope.Scope _scope;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfToolTestHost"/> class.
    /// </summary>
    /// <param name="canShowFigures">Whether the host serves pictures, so tools can show them in the conversation.</param>
    /// <param name="configure">Adds or replaces services.</param>
    public PdfToolTestHost(bool canShowFigures = true, Action<IServiceCollection> configure = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "pdf-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        Interaction = new ChatInteraction { ItemId = "interaction-1" };
        Documents = new InMemoryAIDocumentStore();
        FileStore = new FileSystemFileStore(Root);

        var services = new ServiceCollection();

        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddOptions();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDocumentFileStore>(FileStore);
        services.AddSingleton<IAIDocumentStore>(Documents);
        services.AddSingleton<IGeneratedFileWriterResolver, GeneratedFileWriterResolver>();
        services.AddScoped<IGeneratedDocumentService, DefaultGeneratedDocumentService>();
        services.AddGeneratedFileWriter<PdfGeneratedFileWriter>(".pdf");
        services.AddGeneratedFileWriter<WordGeneratedFileWriter>(".docx");
        services.AddGeneratedFileWriter<SpreadsheetGeneratedFileWriter>(".xlsx");
        services.AddGeneratedFileWriter<DelimitedGeneratedFileWriter>(".csv");
        services.AddGeneratedFileWriter<PlainTextGeneratedFileWriter>(".txt", ".md", ".json", ".html");
        services.AddSingleton<PdfDocumentComposer>();
        services.AddSingleton<IPdfWorkspaceStore, DocumentFileStorePdfWorkspaceStore>();
        services.AddKeyedSingleton<IngestionDocumentReader>(".pdf", new PdfIngestionDocumentReader());

        if (canShowFigures)
        {
            services.AddSingleton<LinkGenerator, FakeLinkGenerator>();
        }

        configure?.Invoke(services);

        Services = services.BuildServiceProvider();

        _scope = AIInvocationScope.Begin();
        _scope.Context.ToolExecutionContext = new AIToolExecutionContext(Interaction);
    }

    /// <summary>
    /// Gets the folder the file store writes to.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Gets the conversation the tools run in.
    /// </summary>
    public ChatInteraction Interaction { get; }

    /// <summary>
    /// Gets the document store.
    /// </summary>
    public InMemoryAIDocumentStore Documents { get; }

    /// <summary>
    /// Gets the file store.
    /// </summary>
    public FileSystemFileStore FileStore { get; }

    /// <summary>
    /// Gets the services.
    /// </summary>
    public ServiceProvider Services { get; }

    /// <summary>
    /// Gets the current invocation.
    /// </summary>
    public AIInvocationContext Invocation => _scope.Context;

    /// <summary>
    /// Attaches a file to the conversation, the way an upload does.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="bytes">The file.</param>
    /// <param name="contentType">The media type.</param>
    /// <returns>The document.</returns>
    public async Task<AIDocument> UploadAsync(string fileName, byte[] bytes, string contentType = "application/pdf")
    {
        var document = new AIDocument
        {
            ItemId = "upload-" + Guid.NewGuid().ToString("N")[..10],
            ReferenceId = Interaction.ItemId,
            ReferenceType = AIReferenceTypes.Document.ChatInteraction,
            FileName = fileName,
            ContentType = contentType,
            FileSize = bytes.Length,
            StoredFilePath = $"documents/chat-interaction/{Interaction.ItemId}/{Guid.NewGuid():N}{Path.GetExtension(fileName)}",
        };

        await using (var stream = new MemoryStream(bytes))
        {
            await FileStore.SaveFileAsync(document.StoredFilePath, stream);
        }

        await Documents.CreateAsync(document);

        return document;
    }

    /// <summary>
    /// Calls a tool.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <param name="arguments">The arguments, as an object or dictionary serialized to JSON.</param>
    /// <returns>The tool's answer.</returns>
    public async Task<string> InvokeAsync(AIFunction tool, object arguments = null)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);

        if (arguments is not null)
        {
            var element = JsonSerializer.SerializeToElement(arguments);

            foreach (var property in element.EnumerateObject())
            {
                values[property.Name] = property.Value.Clone();
            }
        }

        using var scope = Services.CreateScope();

        var result = await tool.InvokeAsync(
            new AIFunctionArguments(values)
            {
                Services = scope.ServiceProvider,
            },
            TestContext.Current.CancellationToken);

        return result?.ToString();
    }

    /// <summary>
    /// Reads the file a <c>[doc:N]</c> or <c>[fig:N]</c> marker links to.
    /// </summary>
    /// <param name="marker">The marker.</param>
    /// <returns>The document and its bytes.</returns>
    public async Task<(AIDocument Document, byte[] Bytes)> ReadMarkerAsync(string marker)
    {
        Assert.True(Invocation.ToolReferences.TryGetValue(marker, out var reference), $"No reference is registered for {marker}.");

        var document = await Documents.FindByIdAsync(reference.ReferenceId);

        Assert.NotNull(document);

        return (document, await ReadFileAsync(document.StoredFilePath));
    }

    /// <summary>
    /// Reads the current bytes of a working PDF.
    /// </summary>
    /// <param name="name">The working PDF's name.</param>
    /// <returns>The file.</returns>
    public async Task<byte[]> ReadWorkingPdfAsync(string name)
    {
        var store = Services.GetRequiredService<IPdfWorkspaceStore>();
        var state = await store.LoadAsync(new PdfWorkspaceScope(Interaction.ItemId, AIReferenceTypes.Document.ChatInteraction));
        var document = state.Find(name);

        Assert.NotNull(document);

        if (document.IsComposed)
        {
            var composer = Services.GetRequiredService<PdfDocumentComposer>();

            return (await composer.ComposeAsync(document.Definition, null, TestContext.Current.CancellationToken)).Bytes;
        }

        return await store.ReadBlobAsync(document.BlobPath);
    }

    /// <summary>
    /// Loads the workspace state.
    /// </summary>
    /// <returns>The state.</returns>
    public Task<PdfWorkspaceState> LoadWorkspaceAsync()
    {
        var store = Services.GetRequiredService<IPdfWorkspaceStore>();

        return store.LoadAsync(new PdfWorkspaceScope(Interaction.ItemId, AIReferenceTypes.Document.ChatInteraction));
    }

    /// <summary>
    /// Reads a stored file.
    /// </summary>
    /// <param name="path">The storage path.</param>
    /// <returns>The bytes.</returns>
    public async Task<byte[]> ReadFileAsync(string path)
    {
        await using var stream = await FileStore.GetFileAsync(path);

        Assert.NotNull(stream);

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        return buffer.ToArray();
    }

    /// <summary>
    /// Ends the invocation and deletes the files.
    /// </summary>
    public void Dispose()
    {
        _scope.Dispose();
        Services.Dispose();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open by a finalizer is left for the temp folder cleanup.
        }
    }

    /// <summary>
    /// Builds download links the way the download endpoint's route does.
    /// </summary>
    private sealed class FakeLinkGenerator : LinkGenerator
    {
        public override string GetPathByAddress<TAddress>(
            HttpContext httpContext,
            TAddress address,
            RouteValueDictionary values,
            RouteValueDictionary ambientValues = null,
            PathString? pathBase = null,
            FragmentString fragment = default,
            LinkOptions options = null)
        {
            return Build(values);
        }

        public override string GetPathByAddress<TAddress>(
            TAddress address,
            RouteValueDictionary values,
            PathString pathBase = default,
            FragmentString fragment = default,
            LinkOptions options = null)
        {
            return Build(values);
        }

        public override string GetUriByAddress<TAddress>(
            HttpContext httpContext,
            TAddress address,
            RouteValueDictionary values,
            RouteValueDictionary ambientValues = null,
            string scheme = null,
            HostString? host = null,
            PathString? pathBase = null,
            FragmentString fragment = default,
            LinkOptions options = null)
        {
            return "https://localhost" + Build(values);
        }

        public override string GetUriByAddress<TAddress>(
            TAddress address,
            RouteValueDictionary values,
            string scheme,
            HostString host,
            PathString pathBase = default,
            FragmentString fragment = default,
            LinkOptions options = null)
        {
            return "https://localhost" + Build(values);
        }

        private static string Build(RouteValueDictionary values)
        {
            return $"/ai/documents/{values["documentId"]}/download";
        }
    }
}

/// <summary>
/// A document store held in memory.
/// </summary>
internal sealed class InMemoryAIDocumentStore : IAIDocumentStore
{
    private readonly List<AIDocument> _documents = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Gets a snapshot of every stored document.
    /// </summary>
    public IReadOnlyList<AIDocument> All
    {
        get
        {
            lock (_lock)
            {
                return [.. _documents];
            }
        }
    }

    public ValueTask CreateAsync(AIDocument entry, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _documents.Add(entry);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> DeleteAsync(AIDocument entry, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(_documents.Remove(entry));
        }
    }

    public ValueTask<AIDocument> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return ValueTask.FromResult(_documents.FirstOrDefault(document => document.ItemId == id));
        }
    }

    public ValueTask<IReadOnlyCollection<AIDocument>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return ValueTask.FromResult<IReadOnlyCollection<AIDocument>>([.. _documents]);
        }
    }

    public ValueTask<IReadOnlyCollection<AIDocument>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var set = ids.ToHashSet(StringComparer.Ordinal);

        lock (_lock)
        {
            return ValueTask.FromResult<IReadOnlyCollection<AIDocument>>([.. _documents.Where(document => set.Contains(document.ItemId))]);
        }
    }

    public Task<IReadOnlyCollection<AIDocument>> GetDocumentsAsync(string referenceId, string referenceType)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyCollection<AIDocument>>([.. _documents.Where(document => document.ReferenceId == referenceId && document.ReferenceType == referenceType)]);
        }
    }

    public ValueTask UpdateAsync(AIDocument entry, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask<PageResult<AIDocument>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext
    {
        throw new NotSupportedException();
    }
}
