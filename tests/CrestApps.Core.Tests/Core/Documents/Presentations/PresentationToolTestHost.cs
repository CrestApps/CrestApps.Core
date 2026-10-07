using System.Text.Json;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.OpenXml;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Ingestion;
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
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Presentations;

/// <summary>
/// Runs presentation tools the way a chat interaction runs them: with a conversation, a file store on disk, a
/// document store, the generated-document service, a download endpoint and the tabular workspace, so a test
/// can upload decks and spreadsheets, call tools, and read back what they produced.
/// </summary>
internal sealed class PresentationToolTestHost : IDisposable
{
    private readonly AIInvocationScope.Scope _scope;

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationToolTestHost"/> class.
    /// </summary>
    /// <param name="canShowFigures">Whether the host serves pictures, so tools can show them in the conversation.</param>
    /// <param name="configure">Adds or replaces services.</param>
    public PresentationToolTestHost(bool canShowFigures = true, Action<IServiceCollection> configure = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "presentation-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        Interaction = new ChatInteraction { ItemId = "interaction-" + Guid.NewGuid().ToString("N")[..8] };
        Documents = new PresentationTestDocumentStore();
        FileStore = new FileSystemFileStore(Root);

        var documentOptions = new ChatDocumentsOptions();
        documentOptions.Add(".csv", embeddable: false, isTabular: true);
        documentOptions.Add(".pptx", embeddable: true, isTabular: false);

        var services = new ServiceCollection();

        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddOptions();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDocumentFileStore>(FileStore);
        services.AddSingleton<IAIDocumentStore>(Documents);
        services.AddSingleton<IGeneratedFileWriterResolver, GeneratedFileWriterResolver>();
        services.AddScoped<IGeneratedDocumentService, DefaultGeneratedDocumentService>();
        services.AddGeneratedFileWriter<WordGeneratedFileWriter>(".docx");
        services.AddGeneratedFileWriter<PlainTextGeneratedFileWriter>(".txt", ".md");
        services.AddGeneratedFileWriter<PlainTextGeneratedFileWriter>(requestable: false, ".svg");

        // The tabular workspace, so slides can take their tables and charts from an uploaded spreadsheet.
        services.AddSingleton<IOptions<ChatDocumentsOptions>>(Options.Create(documentOptions));
        services.AddSingleton<IOptions<DocumentFileSystemFileStoreOptions>>(Options.Create(new DocumentFileSystemFileStoreOptions { BasePath = Root }));
        services.AddSingleton(new Mock<IAIDocumentChunkStore>().Object);
        services.AddSingleton<ITabularDocumentArtifactStore, DocumentFileStoreTabularDocumentArtifactStore>();
        services.AddOptions<TabularWorkspaceOptions>();
        services.AddSingleton<PlainTextIngestionDocumentReader>();
        services.AddKeyedSingleton<IngestionDocumentReader>(".csv", (sp, _) => sp.GetRequiredService<PlainTextIngestionDocumentReader>());
        services.AddScoped<TabularDocumentArtifactFactory>();

        // PowerPoint support: the engine, importers, the .pptx writer and the agent's tools.
        services.AddCoreAIOpenXmlDocumentProcessing();

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
    public PresentationTestDocumentStore Documents { get; }

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
    /// Gets the presentation engine.
    /// </summary>
    public IPresentationEngine Engine => Services.GetRequiredService<IPresentationEngine>();

    /// <summary>
    /// Finds a registered tool by name.
    /// </summary>
    /// <param name="name">The tool's name.</param>
    /// <returns>The tool.</returns>
    public AIFunction Tool(string name)
    {
        var definitions = Services.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value;

        Assert.True(definitions.Tools.TryGetValue(name, out var definition), $"The tool '{name}' is not registered.");

        return (AIFunction)ActivatorUtilities.CreateInstance(Services, definition.ToolType);
    }

    /// <summary>
    /// Attaches a file to the conversation, the way an upload does.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="bytes">The file.</param>
    /// <param name="contentType">The media type.</param>
    /// <returns>The document.</returns>
    public async Task<AIDocument> UploadAsync(string fileName, byte[] bytes, string contentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation")
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
    /// Calls a tool by name.
    /// </summary>
    /// <param name="name">The tool's name.</param>
    /// <param name="arguments">The arguments, as an object or dictionary serialized to JSON.</param>
    /// <returns>The tool's answer.</returns>
    public Task<string> InvokeAsync(string name, object arguments = null)
    {
        return InvokeAsync(Tool(name), arguments);
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
    /// Reads the current package of the active deck, or of the named one.
    /// </summary>
    /// <param name="reference">The deck's name or identifier, or <see langword="null"/> for the active deck.</param>
    /// <returns>The package.</returns>
    public async Task<byte[]> ReadDeckAsync(string reference = null)
    {
        var workspace = await LoadWorkspaceAsync();
        var deck = workspace.FindDeck(reference);

        Assert.NotNull(deck);

        return await workspace.ReadAsync(deck);
    }

    /// <summary>
    /// Loads the conversation's presentation workspace.
    /// </summary>
    /// <returns>The workspace.</returns>
    public Task<PresentationWorkspace> LoadWorkspaceAsync()
    {
        var folder = PresentationWorkspaceStorage.GetFolder(AIReferenceTypes.Document.ChatInteraction, Interaction.ItemId);

        return PresentationWorkspace.LoadAsync(FileStore, folder, Services.GetRequiredService<IOptions<PresentationWorkspaceOptions>>().Value, TimeProvider.System);
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
        catch (UnauthorizedAccessException)
        {
            // Likewise.
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
internal sealed class PresentationTestDocumentStore : IAIDocumentStore
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
