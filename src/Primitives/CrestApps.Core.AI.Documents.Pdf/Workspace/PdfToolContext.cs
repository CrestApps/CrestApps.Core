using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Tooling;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Pdf.Workspace;

/// <summary>
/// What every PDF tool call works with: the conversation it runs in, the files that conversation holds, its
/// PDF workspace, and the ways to hand a result back to the reader.
/// </summary>
/// <remarks>
/// Everything is scoped to the conversation the call runs in. A document, a figure or a picture is only ever
/// read when it belongs to that conversation, whatever identifier the model passes — the identifiers the
/// model sees are not a permission.
/// </remarks>
internal sealed class PdfToolContext : IPdfImageSource
{
    private const string RenderCacheKey = nameof(PdfToolContext) + ".Rendered";

    private readonly IAIDocumentStore _documentStore;
    private readonly IDocumentFileStore _fileStore;
    private readonly IPdfWorkspaceStore _workspaceStore;
    private readonly IGeneratedDocumentService _generatedDocuments;
    private readonly PdfDocumentComposer _composer;
    private readonly IServiceProvider _services;
    private readonly HashSet<string> _readableReferences;
    private bool _readProtected;
    private PdfWorkspaceState _state;

    private PdfToolContext(
        IServiceProvider services,
        IAIDocumentStore documentStore,
        IDocumentFileStore fileStore,
        IPdfWorkspaceStore workspaceStore,
        IGeneratedDocumentService generatedDocuments,
        PdfDocumentComposer composer,
        PdfAgentOptions options,
        PdfWorkspaceScope? scope,
        IReadOnlyList<AIDocument> uploads,
        HashSet<string> readableReferences,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _services = services;
        _documentStore = documentStore;
        _fileStore = fileStore;
        _workspaceStore = workspaceStore;
        _generatedDocuments = generatedDocuments;
        _composer = composer;
        Options = options;
        Scope = scope;
        Uploads = uploads;
        _readableReferences = readableReferences;
        TimeProvider = timeProvider;
        Logger = logger;
    }

    /// <summary>
    /// Gets the agent's limits.
    /// </summary>
    public PdfAgentOptions Options { get; }

    /// <summary>
    /// Gets the conversation the workspace belongs to, or <see langword="null"/> when the call runs outside
    /// a conversation that can keep one.
    /// </summary>
    public PdfWorkspaceScope? Scope { get; }

    /// <summary>
    /// Gets the files the user attached to the conversation, generated files excluded.
    /// </summary>
    public IReadOnlyList<AIDocument> Uploads { get; }

    /// <summary>
    /// Gets the uploaded PDFs.
    /// </summary>
    public IEnumerable<AIDocument> PdfUploads => Uploads.Where(document => IsPdf(document.FileName));

    /// <summary>
    /// Gets the time provider.
    /// </summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// Gets the logger.
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Gets the notes the answer of the current call ends with, such as a warning that a protected source
    /// was saved without its protection.
    /// </summary>
    public List<string> Notes { get; } = [];

    /// <summary>
    /// Gets the request services.
    /// </summary>
    public IServiceProvider Services => _services;

    /// <summary>
    /// Resolves the context for the current AI invocation.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The context, or <see langword="null"/> when the call is not running in a conversation.</returns>
    public static async Task<PdfToolContext> ResolveAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var invocation = AIInvocationScope.Current;
        var execution = invocation?.ToolExecutionContext;

        if (execution is null)
        {
            return null;
        }

        var documentStore = services.GetService<IAIDocumentStore>();
        var fileStore = services.GetService<IDocumentFileStore>();
        var workspaceStore = services.GetService<IPdfWorkspaceStore>();
        var composer = services.GetService<PdfDocumentComposer>();

        if (documentStore is null || fileStore is null || workspaceStore is null || composer is null)
        {
            return null;
        }

        var readScopes = new List<(string ReferenceId, string ReferenceType)>();
        PdfWorkspaceScope? scope = null;

        switch (execution.Resource)
        {
            case ChatInteraction interaction:
                readScopes.Add((interaction.ItemId, AIReferenceTypes.Document.ChatInteraction));
                scope = new PdfWorkspaceScope(interaction.ItemId, AIReferenceTypes.Document.ChatInteraction);

                break;

            case AIProfile profile:
                readScopes.Add((profile.ItemId, AIReferenceTypes.Document.Profile));

                var session = invocation.ChatSession ??
                    (invocation.Items.TryGetValue(nameof(AIChatSession), out var value) ? value as AIChatSession : null);

                if (!string.IsNullOrEmpty(session?.SessionId))
                {
                    readScopes.Add((session.SessionId, AIReferenceTypes.Document.ChatSession));
                    scope = new PdfWorkspaceScope(session.SessionId, AIReferenceTypes.Document.ChatSession);
                }

                break;

            default:
                return null;
        }

        var uploads = new List<AIDocument>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var readable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (referenceId, referenceType) in readScopes)
        {
            if (string.IsNullOrEmpty(referenceId))
            {
                continue;
            }

            readable.Add(referenceId);

            foreach (var document in await documentStore.GetDocumentsAsync(referenceId, referenceType))
            {
                if (document is null ||
                    document.Get<bool>(DefaultGeneratedDocumentService.GeneratedPropertyName) ||
                    !seen.Add(document.ItemId))
                {
                    continue;
                }

                uploads.Add(document);
            }
        }

        return new PdfToolContext(
            services,
            documentStore,
            fileStore,
            workspaceStore,
            services.GetService<IGeneratedDocumentService>(),
            composer,
            services.GetService<IOptions<PdfAgentOptions>>()?.Value ?? new PdfAgentOptions(),
            scope,
            uploads,
            readable,
            services.GetService<TimeProvider>() ?? TimeProvider.System,
            services.GetService<ILoggerFactory>()?.CreateLogger(typeof(PdfToolContext).FullName) ??
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    /// <summary>
    /// Returns whether a file name is a PDF's.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <returns><see langword="true"/> for a <c>.pdf</c> file.</returns>
    public static bool IsPdf(string fileName)
    {
        return string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the workspace scope, or explains why there is none.
    /// </summary>
    /// <returns>The scope.</returns>
    public PdfWorkspaceScope RequireScope()
    {
        return Scope ?? throw new PdfToolException("PDF documents can only be built and edited within an active chat session or chat interaction.");
    }

    /// <summary>
    /// Loads the workspace, once per call.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The workspace.</returns>
    public async Task<PdfWorkspaceState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        if (_state is not null)
        {
            return _state;
        }

        _state = Scope is null
            ? new PdfWorkspaceState()
            : await _workspaceStore.LoadAsync(Scope.Value, cancellationToken);

        return _state;
    }

    /// <summary>
    /// Changes the workspace under its lock, from a freshly loaded copy, and saves it.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="change">The change. It may throw <see cref="PdfToolException"/> to abandon the change.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the change returned.</returns>
    public async Task<T> MutateAsync<T>(Func<PdfWorkspaceState, Task<T>> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        var scope = RequireScope();

        using var gate = await _workspaceStore.LockAsync(scope, cancellationToken);

        // Reloaded under the lock, so a change made by a tool call that ran alongside this one is kept.
        var state = await _workspaceStore.LoadAsync(scope, cancellationToken);
        var result = await change(state);

        await _workspaceStore.SaveAsync(scope, state, cancellationToken);
        _state = state;

        return result;
    }

    /// <summary>
    /// Finds the PDF a tool call names.
    /// </summary>
    /// <param name="handle">The working PDF's name, or an uploaded PDF's file name or document id. When empty, the active working PDF or the only PDF available.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The source.</returns>
    public async Task<PdfSource> FindPdfAsync(string handle, CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(cancellationToken);

        return FindPdf(state, handle);
    }

    /// <summary>
    /// Finds the PDF a tool call names in a given workspace state.
    /// </summary>
    /// <param name="state">The workspace.</param>
    /// <param name="handle">The name.</param>
    /// <returns>The source.</returns>
    public PdfSource FindPdf(PdfWorkspaceState state, string handle)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (string.IsNullOrWhiteSpace(handle))
        {
            var active = state.Find(state.ActiveDocument);

            if (active is not null)
            {
                return PdfSource.ForWorking(active);
            }

            var pdfs = PdfUploads.ToList();

            if (state.Documents.Count == 0 && pdfs.Count == 1)
            {
                return PdfSource.ForUpload(pdfs[0]);
            }

            if (state.Documents.Count == 1 && pdfs.Count == 0)
            {
                return PdfSource.ForWorking(state.Documents[0]);
            }

            throw new PdfToolException(pdfs.Count + state.Documents.Count == 0
                ? "There are no PDFs in this conversation. Upload one, or start a new document with create_pdf."
                : "Several PDFs are available; pass 'pdf' to say which one. " + DescribeAvailable(state));
        }

        var name = handle.Trim();
        var working = state.Find(name);

        if (working is not null)
        {
            return PdfSource.ForWorking(working);
        }

        var upload = FindUpload(name, pdfOnly: true);

        if (upload is not null)
        {
            return PdfSource.ForUpload(upload);
        }

        // A model often drops the extension, or adds one to a working name.
        working = state.Find(Path.GetFileNameWithoutExtension(name));

        if (working is not null)
        {
            return PdfSource.ForWorking(working);
        }

        throw new PdfToolException($"There is no PDF named \"{name}\". " + DescribeAvailable(state));
    }

    /// <summary>
    /// Finds an uploaded file by file name, file name without extension, or document id.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="pdfOnly">Whether only PDFs are considered.</param>
    /// <returns>The upload, or <see langword="null"/>.</returns>
    public AIDocument FindUpload(string name, bool pdfOnly)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim().Trim('"');
        var candidates = pdfOnly ? PdfUploads.ToList() : [.. Uploads];

        return candidates.FirstOrDefault(document => string.Equals(document.ItemId, trimmed, StringComparison.Ordinal))
            ?? candidates.FirstOrDefault(document => string.Equals(document.FileName, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(document => string.Equals(Path.GetFileNameWithoutExtension(document.FileName), trimmed, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(document => string.Equals(Path.GetFileName(document.FileName), Path.GetFileName(trimmed), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Lists the PDFs a tool call can name.
    /// </summary>
    /// <param name="state">The workspace.</param>
    /// <returns>A sentence naming every working and uploaded PDF.</returns>
    public string DescribeAvailable(PdfWorkspaceState state)
    {
        var parts = new List<string>();

        if (state.Documents.Count > 0)
        {
            parts.Add("Working PDFs: " + string.Join(", ", state.Documents.Select(document => $"\"{document.Name}\"")));
        }

        var uploads = PdfUploads.ToList();

        if (uploads.Count > 0)
        {
            parts.Add("Uploaded PDFs: " + string.Join(", ", uploads.Select(document => $"\"{document.FileName}\"")));
        }

        return parts.Count == 0
            ? "No PDFs are available."
            : string.Join(". ", parts) + ".";
    }

    /// <summary>
    /// Reads the bytes of a source, rendering it first when it is a document being composed.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The PDF file.</returns>
    public async Task<byte[]> ReadPdfAsync(PdfSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.IsComposed)
        {
            return (await RenderAsync(source.Working, cancellationToken)).Bytes;
        }

        var bytes = source.IsUpload
            ? await ReadUploadAsync(source.Upload, cancellationToken)
                ?? throw new PdfToolException($"The stored file for \"{source.Name}\" is missing. Ask the user to upload it again.")
            : await _workspaceStore.ReadBlobAsync(source.Working.BlobPath, cancellationToken)
                ?? throw new PdfToolException($"The file behind working PDF \"{source.Name}\" is missing. Recreate it from the upload it came from.");

        // PDFsharp saves every edit without the source's encryption; the answer has to say so.
        _readProtected |= PdfProtection.DeclaresEncryption(bytes);

        return bytes;
    }

    /// <summary>
    /// Reads the stored bytes of an uploaded file.
    /// </summary>
    /// <param name="document">The upload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bytes, or <see langword="null"/> when the file is missing.</returns>
    public async Task<byte[]> ReadUploadAsync(AIDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.StoredFilePath) || !_readableReferences.Contains(document.ReferenceId ?? string.Empty))
        {
            return null;
        }

        if (document.FileSize > Options.MaxDocumentBytes)
        {
            throw new PdfToolException($"\"{document.FileName}\" is {document.FileSize:N0} bytes, larger than the {Options.MaxDocumentBytes:N0} bytes this agent opens.");
        }

        await using var stream = await _fileStore.GetFileAsync(document.StoredFilePath);

        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }

    /// <summary>
    /// Renders a composed working document, reusing a render made earlier in the same turn.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered file.</returns>
    public async Task<PdfCompositionResult> RenderAsync(PdfWorkingDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var key = document.Name + "|" + document.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var invocation = AIInvocationScope.Current;

        if (invocation?.Items.TryGetValue(RenderCacheKey, out var cached) == true &&
            cached is Dictionary<string, PdfCompositionResult> cache &&
            cache.TryGetValue(key, out var hit))
        {
            return hit;
        }

        var result = await ComposeAsync(document.Definition ?? new PdfDocumentDefinition(), cancellationToken);

        if (invocation is not null)
        {
            if (!invocation.Items.TryGetValue(RenderCacheKey, out var store) || store is not Dictionary<string, PdfCompositionResult> renders)
            {
                renders = new Dictionary<string, PdfCompositionResult>(StringComparer.OrdinalIgnoreCase);
                invocation.Items[RenderCacheKey] = renders;
            }

            renders[key] = result;
        }

        return result;
    }

    /// <summary>
    /// Renders a definition, reading its pictures from this conversation.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered file.</returns>
    public Task<PdfCompositionResult> ComposeAsync(PdfDocumentDefinition definition, CancellationToken cancellationToken = default)
    {
        return _composer.ComposeAsync(definition, this, cancellationToken);
    }

    /// <summary>
    /// Saves the result of editing a PDF as a working copy, never over an upload.
    /// </summary>
    /// <param name="state">The workspace, inside <see cref="MutateAsync"/>.</param>
    /// <param name="target">The PDF that was edited.</param>
    /// <param name="saveAs">The name asked for, or <see langword="null"/>.</param>
    /// <param name="bytes">The edited file.</param>
    /// <param name="change">What was done, for the document's history.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The working document the file was saved as.</returns>
    public async Task<PdfWorkingDocument> SaveWorkingFileAsync(
        PdfWorkspaceState state,
        PdfSource target,
        string saveAs,
        byte[] bytes,
        string change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.LongLength > Options.MaxDocumentBytes)
        {
            throw new PdfToolException($"The result is {bytes.LongLength:N0} bytes, larger than the {Options.MaxDocumentBytes:N0} bytes this agent keeps.");
        }

        var name = ChooseWorkingName(state, target, saveAs);
        var existing = state.Find(name);

        if (existing is not null && existing.IsComposed)
        {
            // A composed document is never overwritten by a file: its definition is what later content and
            // formatting changes build on, and replacing it would lose all of that.
            name = UniqueName(state, name + "-edited");
            existing = state.Find(name);
        }

        if (existing is null)
        {
            if (state.Documents.Count >= Options.MaxWorkingDocuments)
            {
                throw new PdfToolException($"This conversation already holds {state.Documents.Count} working PDFs, the most it keeps. Delete some with edit_pdf_pages (operation 'discard') or reuse a name with 'save_as'.");
            }

            existing = new PdfWorkingDocument
            {
                Name = name,
                Kind = PdfWorkingDocument.FileKind,
                SourceFileName = target?.Upload?.FileName ?? target?.Working?.SourceFileName,
                SourceDocumentId = target?.Upload?.ItemId ?? target?.Working?.SourceDocumentId,
            };

            if (target?.Working is not null && !ReferenceEquals(target.Working, existing))
            {
                existing.History.AddRange(target.Working.History);
            }

            state.Documents.Add(existing);
        }

        var previousBlob = existing.BlobPath;

        if (_readProtected && !PdfProtection.DeclaresEncryption(bytes) && !Notes.Contains(PdfProtection.DroppedNote))
        {
            Notes.Add(PdfProtection.DroppedNote);
        }

        existing.BlobPath = await _workspaceStore.WriteBlobAsync(RequireScope(), bytes, ".pdf", cancellationToken);
        existing.ByteLength = bytes.LongLength;
        existing.PageCount = PdfFiles.CountPages(bytes);
        existing.Version++;
        existing.UpdatedUtc = TimeProvider.GetUtcNow().UtcDateTime;

        if (!string.IsNullOrWhiteSpace(change))
        {
            existing.History.Add(change);

            if (existing.History.Count > 50)
            {
                existing.History.RemoveRange(0, existing.History.Count - 50);
            }
        }

        if (!string.IsNullOrEmpty(previousBlob) && !string.Equals(previousBlob, existing.BlobPath, StringComparison.Ordinal))
        {
            await _workspaceStore.DeleteBlobAsync(previousBlob);
        }

        state.ActiveDocument = existing.Name;

        return existing;
    }

    /// <summary>
    /// Picks the name an edited PDF is saved under.
    /// </summary>
    /// <param name="state">The workspace.</param>
    /// <param name="target">The PDF that was edited.</param>
    /// <param name="saveAs">The name asked for, or <see langword="null"/>.</param>
    /// <returns>The name.</returns>
    public static string ChooseWorkingName(PdfWorkspaceState state, PdfSource target, string saveAs)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!string.IsNullOrWhiteSpace(saveAs))
        {
            return SanitizeName(saveAs);
        }

        if (target?.Working is not null && !target.Working.IsComposed)
        {
            // Edits to a working copy land on that copy.
            return target.Working.Name;
        }

        if (target?.Working is not null)
        {
            return UniqueName(state, target.Working.Name + "-edited");
        }

        var baseName = SanitizeName(Path.GetFileNameWithoutExtension(target?.Name) ?? "document");
        var existing = state.Find(baseName);

        // The copy of an upload reuses its name, and carries on being edited under it; a same-named copy of a
        // different upload gets a suffix.
        if (existing is null || string.Equals(existing.SourceDocumentId, target?.Upload?.ItemId, StringComparison.Ordinal))
        {
            return baseName;
        }

        return UniqueName(state, baseName);
    }

    /// <summary>
    /// Makes a name unique within the workspace by adding a number.
    /// </summary>
    /// <param name="state">The workspace.</param>
    /// <param name="name">The name.</param>
    /// <returns>The name, or the name with the first free number appended.</returns>
    public static string UniqueName(PdfWorkspaceState state, string name)
    {
        ArgumentNullException.ThrowIfNull(state);

        var candidate = SanitizeName(name);

        if (state.Find(candidate) is null)
        {
            return candidate;
        }

        for (var number = 2; ; number++)
        {
            var numbered = candidate + "-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (state.Find(numbered) is null)
            {
                return numbered;
            }
        }
    }

    /// <summary>
    /// Cleans a name for use as a working PDF name and a file name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The cleaned name.</returns>
    public static string SanitizeName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();

        if (trimmed.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(trimmed.Length);

        foreach (var character in trimmed)
        {
            builder.Append(invalid.Contains(character) || char.IsControl(character) ? '-' : character);
        }

        var cleaned = builder.ToString().Trim(' ', '-', '.');

        if (cleaned.Length > 80)
        {
            cleaned = cleaned[..80].Trim(' ', '-', '.');
        }

        return cleaned.Length == 0 ? "document" : cleaned;
    }

    /// <summary>
    /// Stores a picture in the workspace so composed documents can place it.
    /// </summary>
    /// <param name="state">The workspace, inside <see cref="MutateAsync"/>.</param>
    /// <param name="bytes">The picture.</param>
    /// <param name="mediaType">The media type.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="description">What the picture is.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The asset.</returns>
    public async Task<PdfWorkspaceAsset> AddAssetAsync(
        PdfWorkspaceState state,
        byte[] bytes,
        string mediaType,
        string fileName,
        string description,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(bytes);

        var asset = new PdfWorkspaceAsset
        {
            Id = "img" + state.NextAssetNumber++.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FileName = fileName,
            MediaType = mediaType,
            ByteLength = bytes.LongLength,
            Description = description,
            BlobPath = await _workspaceStore.WriteBlobAsync(RequireScope(), bytes, Path.GetExtension(fileName), cancellationToken),
        };

        state.Assets.Add(asset);

        return asset;
    }

    /// <summary>
    /// Deletes a stored workspace file.
    /// </summary>
    /// <param name="path">The path.</param>
    public Task DeleteBlobAsync(string path)
    {
        return _workspaceStore.DeleteBlobAsync(path);
    }

    /// <summary>
    /// Stores a file as a download attached to the conversation.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="bytes">The file.</param>
    /// <param name="contentType">The media type it is served with, or <see langword="null"/> to infer it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <c>[doc:N]</c> marker that links it, or <see langword="null"/> when there is no invocation to register it on.</returns>
    public async Task<string> ExportAsync(string fileName, byte[] bytes, string contentType = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var scope = RequireScope();

        if (_generatedDocuments is null)
        {
            throw new PdfToolException("Downloads are not available on this host.");
        }

        var result = await _generatedDocuments.CreateAsync(
            new GeneratedDocumentRequest(
                scope.ReferenceId,
                scope.ReferenceType,
                fileName,
                new GeneratedFileContent
                {
                    Title = Path.GetFileNameWithoutExtension(fileName),
                    EncodedContent = bytes,
                })
            {
                ContentType = contentType,
            },
            cancellationToken);

        return result.ReferenceToken;
    }

    /// <summary>
    /// Shows pictures in the conversation where their markers are written.
    /// </summary>
    /// <param name="figures">The pictures.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One <c>[fig:N]</c> marker per picture, or <see langword="null"/> when this host cannot show pictures here.</returns>
    public async Task<List<string>> ShowFiguresAsync(IReadOnlyList<PdfFigure> figures, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(figures);

        var invocation = AIInvocationScope.Current;

        // A marker registered without an address this host serves reaches the reader as a broken picture, so
        // the check is whether the pictures can be delivered, before any of them is stored.
        if (invocation is null ||
            Scope is null ||
            _generatedDocuments is null ||
            figures.Count == 0 ||
            string.IsNullOrEmpty(ResolveDownloadLink("probe")))
        {
            return null;
        }

        var index = FigureReferenceMarker.NextIndex(invocation);
        var pending = new List<(string Marker, AICompletionReference Reference)>(figures.Count);

        foreach (var figure in figures)
        {
            var result = await _generatedDocuments.CreateAsync(
                new GeneratedDocumentRequest(
                    Scope.Value.ReferenceId,
                    Scope.Value.ReferenceType,
                    figure.FileName,
                    new GeneratedFileContent
                    {
                        Title = figure.Title,
                        EncodedContent = figure.Bytes,
                    })
                {
                    // Shown where its marker sits, so it is not also listed as a download nobody asked for.
                    RegisterDownloadReference = false,
                },
                cancellationToken);

            var link = ResolveDownloadLink(result.Document.ItemId);

            if (string.IsNullOrEmpty(link))
            {
                return null;
            }

            var marker = FigureReferenceMarker.Format(index);

            pending.Add((marker, new AICompletionReference
            {
                Text = figure.Title,
                Title = figure.Title,
                Link = link,
                IsImage = true,
                Index = index,
                ReferenceId = result.Document.ItemId,
                ReferenceType = AIReferenceTypes.DataSource.Document,
            }));

            index++;
        }

        foreach (var (marker, reference) in pending)
        {
            invocation.ToolReferences[marker] = reference;

            // A spoken reply never contains the marker, so the host is asked to show the picture as well.
            invocation.RequestFigureDisplay(marker);
        }

        return [.. pending.Select(entry => entry.Marker)];
    }

    /// <summary>
    /// Builds the address the host serves a document from.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <returns>The link, or <see langword="null"/> when the host exposes no download endpoint.</returns>
    public string ResolveDownloadLink(string documentId)
    {
        var linkGenerator = _services.GetService<LinkGenerator>();

        if (linkGenerator is null)
        {
            return null;
        }

        var values = new RouteValueDictionary
        {
            ["documentId"] = documentId,
        };

        try
        {
            var httpContext = _services.GetService<IHttpContextAccessor>()?.HttpContext;

            return httpContext is null
                ? linkGenerator.GetPathByName(DownloadAIDocument.DefaultRouteName, values)
                : linkGenerator.GetPathByName(httpContext, DownloadAIDocument.DefaultRouteName, values);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a picture a composed document refers to, from this conversation only.
    /// </summary>
    /// <param name="source">The picture source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The picture, or <see langword="null"/>.</returns>
    public async Task<PdfImageData> ResolveAsync(string source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        try
        {
            var bytes = await ResolveImageBytesAsync(source.Trim(), cancellationToken);

            if (bytes is null || bytes.Length == 0 || bytes.Length > Options.MaxImageBytes)
            {
                return null;
            }

            return PdfImageInfo.TryRead(bytes, out var mediaType, out _, out _)
                ? new PdfImageData(bytes, mediaType, source)
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(ex, "The PDF image source '{Source}' could not be resolved.", source);
            }

            return null;
        }
    }

    private async Task<byte[]> ResolveImageBytesAsync(string source, CancellationToken cancellationToken)
    {
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = source.IndexOf(',', StringComparison.Ordinal);

            if (comma < 0 || !source[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Convert.FromBase64String(source[(comma + 1)..]);
        }

        if (source.StartsWith("asset:", StringComparison.OrdinalIgnoreCase))
        {
            var asset = (await GetStateAsync(cancellationToken)).FindAsset(source);

            return asset is null
                ? null
                : await _workspaceStore.ReadBlobAsync(asset.BlobPath, cancellationToken);
        }

        if (source.StartsWith(FigureReferenceMarker.Prefix, StringComparison.OrdinalIgnoreCase) &&
            AIInvocationScope.Current?.ToolReferences.TryGetValue(source, out var reference) == true &&
            !string.IsNullOrEmpty(reference.ReferenceId))
        {
            return await ReadDocumentBytesAsync(reference.ReferenceId, cancellationToken);
        }

        if (source.StartsWith("figure:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = source["figure:".Length..].Split('/', 2, StringSplitOptions.TrimEntries);

            return parts.Length == 2
                ? await ReadFigureBytesAsync(parts[0], parts[1], cancellationToken)
                : null;
        }

        // A link this host serves names the document it serves.
        var documentsSegment = source.IndexOf("/ai/documents/", StringComparison.OrdinalIgnoreCase);

        if (documentsSegment >= 0)
        {
            var path = source[(documentsSegment + "/ai/documents/".Length)..].Split('?', '#')[0];
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length >= 3 && string.Equals(segments[1], "figures", StringComparison.OrdinalIgnoreCase))
            {
                return await ReadFigureBytesAsync(Uri.UnescapeDataString(segments[0]), Uri.UnescapeDataString(segments[2]), cancellationToken);
            }

            if (segments.Length >= 1)
            {
                return await ReadDocumentBytesAsync(Uri.UnescapeDataString(segments[0]), cancellationToken);
            }
        }

        var upload = FindUpload(source, pdfOnly: false);

        if (upload is not null)
        {
            return await ReadUploadAsync(upload, cancellationToken);
        }

        return await ReadDocumentBytesAsync(source, cancellationToken);
    }

    private async Task<byte[]> ReadDocumentBytesAsync(string documentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentId) || documentId.Length > 64)
        {
            return null;
        }

        var document = await _documentStore.FindByIdAsync(documentId, cancellationToken);

        if (document is null || !_readableReferences.Contains(document.ReferenceId ?? string.Empty))
        {
            return null;
        }

        return await ReadStoredFileAsync(document.StoredFilePath, cancellationToken);
    }

    private async Task<byte[]> ReadFigureBytesAsync(string documentId, string figureId, CancellationToken cancellationToken)
    {
        var document = await _documentStore.FindByIdAsync(documentId, cancellationToken);

        if (document is null || !_readableReferences.Contains(document.ReferenceId ?? string.Empty))
        {
            return null;
        }

        var figure = document.FindFigure(figureId);

        return figure is null
            ? null
            : await ReadStoredFileAsync(figure.StoragePath, cancellationToken);
    }

    private async Task<byte[]> ReadStoredFileAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        await using var stream = await _fileStore.GetFileAsync(path);

        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }
}
