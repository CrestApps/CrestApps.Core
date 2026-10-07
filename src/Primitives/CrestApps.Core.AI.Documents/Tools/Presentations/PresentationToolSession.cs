using System.Globalization;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// One presentation tool call's hold on its conversation's workspace: the decks, the engine, the uploads it
/// can draw pictures and templates from, and the lock that keeps other calls out until it is done.
/// </summary>
internal sealed class PresentationToolSession : IAsyncDisposable
{
    private readonly IDisposable _lock;
    private readonly IDocumentFileStore _fileStore;

    private PresentationToolSession(
        IServiceProvider services,
        IPresentationEngine engine,
        PresentationWorkspace workspace,
        IDisposable workspaceLock,
        IDocumentFileStore fileStore,
        GeneratedDocumentScope scope,
        IReadOnlyList<AIDocument> documents,
        PresentationWorkspaceOptions options)
    {
        Services = services;
        Engine = engine;
        Workspace = workspace;
        _lock = workspaceLock;
        _fileStore = fileStore;
        ReferenceId = scope.ReferenceId;
        ReferenceType = scope.ReferenceType;
        Documents = documents;
        Options = options;
    }

    /// <summary>
    /// Gets the request services.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the engine that reads and edits decks.
    /// </summary>
    public IPresentationEngine Engine { get; }

    /// <summary>
    /// Gets the conversation's workspace.
    /// </summary>
    public PresentationWorkspace Workspace { get; }

    /// <summary>
    /// Gets the conversation's identifier.
    /// </summary>
    public string ReferenceId { get; }

    /// <summary>
    /// Gets the conversation's reference type.
    /// </summary>
    public string ReferenceType { get; }

    /// <summary>
    /// Gets the documents the conversation can use: its uploads and its profile's documents, not files it
    /// generated.
    /// </summary>
    public IReadOnlyList<AIDocument> Documents { get; }

    /// <summary>
    /// Gets the workspace options.
    /// </summary>
    public PresentationWorkspaceOptions Options { get; }

    /// <summary>
    /// Gets the names of decks imported from uploads while preparing this call.
    /// </summary>
    public List<string> Imported { get; } = [];

    /// <summary>
    /// Gets problems met while importing uploads.
    /// </summary>
    public List<string> ImportProblems { get; } = [];

    /// <summary>
    /// Opens the workspace of the conversation the tool is running in, importing any new uploads.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The session, or an explanation of why there is none.</returns>
    public static async Task<(PresentationToolSession Session, string Error)> OpenAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var engine = services.GetService<IPresentationEngine>();

        if (engine is null)
        {
            return (null, "PowerPoint support is not enabled on this host.");
        }

        if (GeneratedDocumentScope.Resolve() is not { } scope)
        {
            return (null, "Presentations are only available within an active chat session or chat interaction.");
        }

        var folder = PresentationWorkspaceStorage.GetFolder(scope.ReferenceType, scope.ReferenceId);

        if (folder is null)
        {
            return (null, "This conversation cannot hold a presentation workspace.");
        }

        var fileStore = services.GetRequiredService<IDocumentFileStore>();
        var options = services.GetRequiredService<IOptions<PresentationWorkspaceOptions>>().Value;
        var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var workspaceLock = await PresentationWorkspace.AcquireAsync(folder, cancellationToken);

        try
        {
            var workspace = await PresentationWorkspace.LoadAsync(fileStore, folder, options, timeProvider);
            var documents = await LoadDocumentsAsync(services, scope);
            var session = new PresentationToolSession(services, engine, workspace, workspaceLock, fileStore, scope, documents, options);

            await session.ImportUploadsAsync(cancellationToken);

            return (session, null);
        }
        catch
        {
            workspaceLock.Dispose();

            throw;
        }
    }

    /// <summary>
    /// Finds the deck a call names, or the active deck.
    /// </summary>
    /// <param name="reference">The deck's name or identifier.</param>
    /// <returns>The deck.</returns>
    /// <exception cref="PresentationArgumentException">No such deck exists.</exception>
    public PresentationDeckState RequireDeck(string reference)
    {
        var deck = Workspace.FindDeck(reference);

        if (deck is not null)
        {
            return deck;
        }

        if (Workspace.State.Decks.Count == 0)
        {
            throw new PresentationArgumentException("There is no presentation in this conversation yet. Start one with create_presentation, or ask the user to upload a .pptx file.");
        }

        throw new PresentationArgumentException($"There is no presentation named \"{reference}\". The presentations are: {string.Join(", ", Workspace.State.Decks.Select(candidate => $"\"{candidate.Name}\" ({candidate.Id})"))}.");
    }

    /// <summary>
    /// Reads a deck.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <param name="options">How much to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The model.</returns>
    public async Task<PresentationModel> ReadAsync(PresentationDeckState deck, PresentationReadOptions options, CancellationToken cancellationToken)
    {
        var package = await Workspace.ReadAsync(deck)
            ?? throw new PresentationArgumentException($"The file of the presentation \"{deck.Name}\" is missing from the workspace.");

        return await Engine.ReadAsync(package, options ?? PresentationReadOptions.TextOnly, cancellationToken);
    }

    /// <summary>
    /// Applies edits to a deck and saves the result as its new version.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <param name="edits">The edits.</param>
    /// <param name="description">A short description of the change, for undo.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What changed.</returns>
    public async Task<PresentationEditResult> ApplyAsync(PresentationDeckState deck, IReadOnlyList<PresentationEdit> edits, string description, CancellationToken cancellationToken)
    {
        var package = await Workspace.ReadAsync(deck)
            ?? throw new PresentationArgumentException($"The file of the presentation \"{deck.Name}\" is missing from the workspace.");

        var result = await Engine.EditAsync(package, edits, new PresentationEditContext { Formatting = deck.Formatting ?? new PresentationFormatting() }, cancellationToken);

        await Workspace.SaveAsync(deck, result.Package, description);

        return result;
    }

    /// <summary>
    /// Finds a conversation document by identifier or file name.
    /// </summary>
    /// <param name="reference">The document's identifier or file name.</param>
    /// <returns>The document, or <see langword="null"/>.</returns>
    public AIDocument FindDocument(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var trimmed = reference.Trim();

        return Documents.FirstOrDefault(document => string.Equals(document.ItemId, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? Documents.FirstOrDefault(document => string.Equals(document.FileName, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? Documents.FirstOrDefault(document => string.Equals(Path.GetFileNameWithoutExtension(document.FileName), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads the bytes of a conversation document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The bytes, or <see langword="null"/> when the file is missing.</returns>
    public async Task<byte[]> ReadDocumentAsync(AIDocument document)
    {
        if (string.IsNullOrWhiteSpace(document?.StoredFilePath))
        {
            return null;
        }

        await using var stream = await _fileStore.GetFileAsync(document.StoredFilePath);

        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        return buffer.ToArray();
    }

    /// <summary>
    /// Loads an uploaded picture to place on a slide.
    /// </summary>
    /// <param name="reference">The picture's document identifier or file name.</param>
    /// <returns>The picture.</returns>
    /// <exception cref="PresentationArgumentException">The picture cannot be used.</exception>
    public async Task<PresentationImageData> LoadImageAsync(string reference)
    {
        var document = FindDocument(reference);

        if (document is null)
        {
            var images = Documents.Where(candidate => MediaTypeHelper.IsVisionImageExtension(Path.GetExtension(candidate.FileName))).Select(candidate => $"\"{candidate.FileName}\" ({candidate.ItemId})").ToList();

            throw new PresentationArgumentException(images.Count == 0
                ? $"There is no uploaded picture \"{reference}\" in this conversation. Ask the user to upload one, or use image_prompt to generate one."
                : $"There is no uploaded picture \"{reference}\". The pictures available are: {string.Join(", ", images)}.");
        }

        var data = await ReadDocumentAsync(document)
            ?? throw new PresentationArgumentException($"The file \"{document.FileName}\" could not be read.");

        if (data.Length > Options.MaxImageBytes)
        {
            throw new PresentationArgumentException($"\"{document.FileName}\" is {(data.Length / 1024d / 1024d).ToString("0.#", CultureInfo.InvariantCulture)} MB, larger than the {(Options.MaxImageBytes / 1024d / 1024d).ToString("0.#", CultureInfo.InvariantCulture)} MB a slide picture may be.");
        }

        if (!PresentationImageInfo.TryRead(data, out var contentType, out var width, out var height))
        {
            throw new PresentationArgumentException($"\"{document.FileName}\" is not a PNG, JPEG, GIF or BMP picture, so it cannot be placed on a slide.");
        }

        return new PresentationImageData
        {
            Data = data,
            ContentType = contentType,
            FileName = document.FileName,
            PixelWidth = width,
            PixelHeight = height,
        };
    }

    /// <summary>
    /// Releases the workspace.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _lock.Dispose();

        return ValueTask.CompletedTask;
    }

    private static async Task<IReadOnlyList<AIDocument>> LoadDocumentsAsync(IServiceProvider services, GeneratedDocumentScope scope)
    {
        var store = services.GetService<IAIDocumentStore>();

        if (store is null)
        {
            return [];
        }

        var documents = new List<AIDocument>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var scopes = new List<(string Id, string Type)> { (scope.ReferenceId, scope.ReferenceType) };

        // A profile's own documents, such as a company template, are there to be used, but are not imported as
        // decks the way a conversation's uploads are.
        if (AIInvocationScope.Current?.ToolExecutionContext?.Resource is AIProfile profile && !string.IsNullOrEmpty(profile.ItemId))
        {
            scopes.Add((profile.ItemId, AIReferenceTypes.Document.Profile));
        }

        foreach (var (id, type) in scopes)
        {
            foreach (var document in await store.GetDocumentsAsync(id, type))
            {
                if (document.Get<bool>(DefaultGeneratedDocumentService.GeneratedPropertyName) || !seen.Add(document.ItemId))
                {
                    continue;
                }

                documents.Add(document);
            }
        }

        return documents;
    }

    private async Task ImportUploadsAsync(CancellationToken cancellationToken)
    {
        var logger = Services.GetService<ILoggerFactory>()?.CreateLogger<PresentationToolSession>();

        foreach (var document in Documents)
        {
            if (document.ReferenceType != ReferenceType ||
                document.ReferenceId != ReferenceId ||
                !Options.IsPresentationFile(document.FileName) ||
                Workspace.State.DismissedDocumentIds.Contains(document.ItemId) ||
                Workspace.State.Decks.Any(deck => deck.SourceDocumentId == document.ItemId))
            {
                continue;
            }

            var package = await ImportAsync(document, cancellationToken);

            if (package is null)
            {
                ImportProblems.Add($"The upload \"{document.FileName}\" could not be read as a presentation.");
                continue;
            }

            try
            {
                var deck = await Workspace.AddAsync(Path.GetFileNameWithoutExtension(document.FileName), package, document.ItemId, document.FileName);
                deck.FileName = Path.GetFileNameWithoutExtension(document.FileName) + ".pptx";
                Imported.Add(deck.Name);
            }
            catch (PresentationEditException exception)
            {
                ImportProblems.Add(exception.Message);
            }

            if (logger?.IsEnabled(LogLevel.Debug) == true)
            {
                logger.LogDebug("Imported the uploaded presentation '{DocumentId}' into the workspace of '{ReferenceId}'.", document.ItemId, ReferenceId);
            }
        }

        if (Imported.Count > 0)
        {
            await Workspace.SaveStateAsync();
        }
    }

    /// <summary>
    /// Imports an uploaded deck or template as a package the workspace can edit.
    /// </summary>
    /// <param name="document">The upload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The package, or <see langword="null"/> when it cannot be read.</returns>
    public async Task<byte[]> ImportAsync(AIDocument document, CancellationToken cancellationToken)
    {
        var importer = Services.GetKeyedService<IPresentationImporter>(Path.GetExtension(document.FileName)?.ToLowerInvariant());

        if (importer is null)
        {
            return null;
        }

        await using var stream = await _fileStore.GetFileAsync(document.StoredFilePath);

        return stream is null ? null : await importer.ImportAsync(stream, document.FileName, cancellationToken);
    }
}
