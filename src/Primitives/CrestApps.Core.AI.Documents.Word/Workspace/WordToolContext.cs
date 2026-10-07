using System.Globalization;
using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Tooling;
using CrestApps.Core.AI.Documents.Word.Editing;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Word.Workspace;

/// <summary>
/// What every Word tool call works with: the conversation it runs in, the files that conversation holds, its
/// Word workspace, and the ways to hand a result back to the reader.
/// </summary>
/// <remarks>
/// Everything is scoped to the conversation the call runs in. A document, a figure or a picture is only ever
/// read when it belongs to that conversation, whatever identifier the model passes — the identifiers the model
/// sees are not a permission. An uploaded file is never written: the first change to one saves a working copy.
/// </remarks>
internal sealed class WordToolContext
{
    private readonly IAIDocumentStore _documentStore;
    private readonly IDocumentFileStore _fileStore;
    private readonly IWordWorkspaceStore _workspaceStore;
    private readonly IGeneratedDocumentService _generatedDocuments;
    private readonly HashSet<string> _readableReferences;
    private WordWorkspaceState _state;
    private Mutation _mutation;

    private WordToolContext(
        IServiceProvider services,
        IAIDocumentStore documentStore,
        IDocumentFileStore fileStore,
        IWordWorkspaceStore workspaceStore,
        IGeneratedDocumentService generatedDocuments,
        WordAgentOptions options,
        WordWorkspaceScope? scope,
        IReadOnlyList<AIDocument> uploads,
        HashSet<string> readableReferences,
        TimeProvider timeProvider,
        ILogger logger)
    {
        Services = services;
        _documentStore = documentStore;
        _fileStore = fileStore;
        _workspaceStore = workspaceStore;
        _generatedDocuments = generatedDocuments;
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
    public WordAgentOptions Options { get; }

    /// <summary>
    /// Gets the conversation the workspace belongs to, or <see langword="null"/> when the call runs outside a
    /// conversation that can keep one.
    /// </summary>
    public WordWorkspaceScope? Scope { get; }

    /// <summary>
    /// Gets the files the user attached to the conversation, generated files excluded.
    /// </summary>
    public IReadOnlyList<AIDocument> Uploads { get; }

    /// <summary>
    /// Gets the uploaded Word documents.
    /// </summary>
    public IEnumerable<AIDocument> WordUploads => Uploads.Where(document => WordPackage.IsWordFile(document.FileName));

    /// <summary>
    /// Gets the time provider.
    /// </summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// Gets the logger.
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Gets the notes the answer of the current call ends with.
    /// </summary>
    public List<string> Notes { get; } = [];

    /// <summary>
    /// Gets the request services.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Resolves the context for the current AI invocation.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The context, or <see langword="null"/> when the call is not running in a conversation.</returns>
    public static async Task<WordToolContext> ResolveAsync(IServiceProvider services, CancellationToken cancellationToken = default)
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
        var workspaceStore = services.GetService<IWordWorkspaceStore>();

        if (documentStore is null || fileStore is null || workspaceStore is null)
        {
            return null;
        }

        var readScopes = new List<(string ReferenceId, string ReferenceType)>();
        WordWorkspaceScope? scope = null;

        switch (execution.Resource)
        {
            case ChatInteraction interaction:
                readScopes.Add((interaction.ItemId, AIReferenceTypes.Document.ChatInteraction));
                scope = new WordWorkspaceScope(interaction.ItemId, AIReferenceTypes.Document.ChatInteraction);

                break;

            case AIProfile profile:
                readScopes.Add((profile.ItemId, AIReferenceTypes.Document.Profile));

                var session = invocation.ChatSession ??
                    (invocation.Items.TryGetValue(nameof(AIChatSession), out var value) ? value as AIChatSession : null);

                if (!string.IsNullOrEmpty(session?.SessionId))
                {
                    readScopes.Add((session.SessionId, AIReferenceTypes.Document.ChatSession));
                    scope = new WordWorkspaceScope(session.SessionId, AIReferenceTypes.Document.ChatSession);
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

        return new WordToolContext(
            services,
            documentStore,
            fileStore,
            workspaceStore,
            services.GetService<IGeneratedDocumentService>(),
            services.GetService<IOptions<WordAgentOptions>>()?.Value ?? new WordAgentOptions(),
            scope,
            uploads,
            readable,
            services.GetService<TimeProvider>() ?? TimeProvider.System,
            services.GetService<ILoggerFactory>()?.CreateLogger(typeof(WordToolContext).FullName) ?? NullLogger.Instance);
    }

    /// <summary>
    /// Gets the workspace scope, or explains why there is none.
    /// </summary>
    /// <returns>The scope.</returns>
    public WordWorkspaceScope RequireScope()
    {
        return Scope ?? throw new WordToolException("Word documents can only be built and edited within an active chat session or chat interaction.");
    }

    /// <summary>
    /// Loads the workspace, once per call.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The workspace.</returns>
    public async Task<WordWorkspaceState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        if (_state is not null)
        {
            return _state;
        }

        _state = Scope is null
            ? new WordWorkspaceState()
            : await _workspaceStore.LoadAsync(Scope.Value, cancellationToken);

        return _state;
    }

    /// <summary>
    /// Changes the workspace under its lock, from a freshly loaded copy, and saves it.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="change">The change. It may throw <see cref="WordToolException"/> to abandon the change.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the change returned.</returns>
    /// <remarks>
    /// A stored file the change replaces or removes is deleted only after the workspace that no longer refers to
    /// it is saved, so a change that is cancelled or fails part way never leaves a working document pointing at a
    /// deleted file. Once the change has run, the workspace is saved even if the call is cancelled meanwhile.
    /// </remarks>
    public async Task<T> MutateAsync<T>(Func<WordWorkspaceState, Task<T>> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (_mutation is not null)
        {
            throw new InvalidOperationException("The Word workspace is already being changed by this call; make the change inside the one in progress.");
        }

        var scope = RequireScope();

        using var gate = await _workspaceStore.LockAsync(scope, cancellationToken);

        // Reloaded under the lock, so a change made by a tool call that ran alongside this one is kept.
        var state = await _workspaceStore.LoadAsync(scope, cancellationToken);
        var previous = _state;
        var mutation = new Mutation(state);

        _state = state;
        _mutation = mutation;

        try
        {
            T result;

            try
            {
                result = await change(state);
            }
            catch
            {
                _state = previous;

                // Nothing refers to the files the abandoned change wrote.
                foreach (var path in mutation.Written)
                {
                    await _workspaceStore.DeleteBlobAsync(path);
                }

                throw;
            }

            try
            {
                await _workspaceStore.SaveAsync(scope, state, CancellationToken.None);
            }
            catch
            {
                // Whether the stored workspace now names the new files or the old ones is unknown, so both are
                // kept: a file left behind costs storage, a deleted one would break the document.
                _state = previous;

                throw;
            }

            await DeleteReplacedBlobsAsync(scope, state, mutation.Replaced);

            return result;
        }
        finally
        {
            _mutation = null;
        }
    }

    /// <summary>
    /// Opens a document, applies an edit to it and saves the result as a new version of a working document.
    /// The edit is all or nothing: when it throws, nothing is saved. An edit that clears
    /// <see cref="WordEditContext.Changed"/> saves no new version; on an upload it ends with a
    /// <see cref="WordToolException"/>, since there is no working document to report.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="handle">The document's name, or <see langword="null"/> for the active document.</param>
    /// <param name="change">What was done, for the document's history.</param>
    /// <param name="edit">The edit.</param>
    /// <param name="saveAs">The name to save the result under, or <see langword="null"/> to keep the document's own.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What the edit returned, and the working document the result was saved as.</returns>
    public Task<(T Result, WordWorkingDocument Document)> EditAsync<T>(
        string handle,
        string change,
        Func<WordEditContext, Task<T>> edit,
        string saveAs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        return MutateAsync(async state =>
        {
            var source = FindDocument(state, handle);
            var bytes = await ReadBytesAsync(source, cancellationToken);

            using var package = OpenPackage(bytes, source);

            var context = new WordEditContext(package, source, Options.Author, TimeProvider.GetUtcNow().UtcDateTime);
            var result = await edit(context);

            if (!context.Changed)
            {
                // An upload has no working document to report, and none is made when nothing changed.
                return (result, source.Working ?? throw new WordToolException($"Nothing in {source.Describe()} needed changing, so no working copy was saved."));
            }

            var design = context.DesignChanged ? context.Design : null;
            var saved = await SaveWorkingAsync(state, source, saveAs, package.Save(), change, design, cancellationToken);

            return (result, saved);
        }, cancellationToken);
    }

    /// <summary>
    /// Opens a document for reading. The caller disposes it; nothing it changes is saved.
    /// </summary>
    /// <param name="source">The document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The open document.</returns>
    public async Task<WordPackage> OpenAsync(WordSource source, CancellationToken cancellationToken = default)
    {
        return OpenPackage(await ReadBytesAsync(source, cancellationToken), source);
    }

    /// <summary>
    /// Finds the document a tool call names.
    /// </summary>
    /// <param name="handle">The working document's name, or an uploaded file's name or id. When empty, the active working document or the only document available.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The source.</returns>
    public async Task<WordSource> FindDocumentAsync(string handle, CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(cancellationToken);

        return FindDocument(state, handle);
    }

    /// <summary>
    /// Finds the document a tool call names in a given workspace state.
    /// </summary>
    /// <param name="state">The workspace.</param>
    /// <param name="handle">The name.</param>
    /// <returns>The source.</returns>
    public WordSource FindDocument(WordWorkspaceState state, string handle)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (string.IsNullOrWhiteSpace(handle))
        {
            var active = state.Find(state.ActiveDocument);

            if (active is not null)
            {
                return WordSource.ForWorking(active);
            }

            var uploads = WordUploads.ToList();

            if (state.Documents.Count == 0 && uploads.Count == 1)
            {
                return WordSource.ForUpload(uploads[0]);
            }

            if (state.Documents.Count == 1 && uploads.Count == 0)
            {
                return WordSource.ForWorking(state.Documents[0]);
            }

            throw new WordToolException(uploads.Count + state.Documents.Count == 0
                ? "There are no Word documents in this conversation. Upload one, or start a new document with create_word_document."
                : "Several Word documents are available; pass 'document' to say which one. " + DescribeAvailable(state));
        }

        var name = handle.Trim();
        var working = state.Find(name);

        if (working is not null)
        {
            return WordSource.ForWorking(working);
        }

        var upload = FindUpload(name, wordOnly: true);

        // A copy of an upload is edited under the upload's name, so naming the upload after it was edited
        // means the working copy that holds those edits.
        if (upload is not null)
        {
            var copy = state.Documents.FirstOrDefault(document => string.Equals(document.SourceDocumentId, upload.ItemId, StringComparison.Ordinal) &&
                string.Equals(document.Name, SanitizeName(Path.GetFileNameWithoutExtension(upload.FileName)), StringComparison.OrdinalIgnoreCase));

            return copy is null ? WordSource.ForUpload(upload) : WordSource.ForWorking(copy);
        }

        // A model often drops the extension, or adds one to a working name.
        working = state.Find(Path.GetFileNameWithoutExtension(name));

        if (working is not null)
        {
            return WordSource.ForWorking(working);
        }

        throw new WordToolException($"There is no Word document named \"{name}\". " + DescribeAvailable(state));
    }

    /// <summary>
    /// Finds the original of a document: the upload a working copy was made from, or the upload itself.
    /// </summary>
    /// <param name="handle">The document's name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The upload, or <see langword="null"/> when the document was not copied from one.</returns>
    public async Task<WordSource> FindOriginalAsync(string handle, CancellationToken cancellationToken = default)
    {
        var source = await FindDocumentAsync(handle, cancellationToken);

        if (source.IsUpload)
        {
            return source;
        }

        var upload = Uploads.FirstOrDefault(document => string.Equals(document.ItemId, source.Working.SourceDocumentId, StringComparison.Ordinal));

        return upload is null ? null : WordSource.ForUpload(upload);
    }

    /// <summary>
    /// Finds an uploaded file by file name, file name without extension, or document id.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="wordOnly">Whether only Word documents are considered.</param>
    /// <returns>The upload, or <see langword="null"/>.</returns>
    public AIDocument FindUpload(string name, bool wordOnly)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim().Trim('"');
        var candidates = wordOnly ? WordUploads.ToList() : [.. Uploads];

        return candidates.FirstOrDefault(document => string.Equals(document.ItemId, trimmed, StringComparison.Ordinal))
            ?? candidates.FirstOrDefault(document => string.Equals(document.FileName, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(document => string.Equals(Path.GetFileNameWithoutExtension(document.FileName), trimmed, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(document => string.Equals(Path.GetFileName(document.FileName), Path.GetFileName(trimmed), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Lists the documents a tool call can name.
    /// </summary>
    /// <param name="state">The workspace.</param>
    /// <returns>A sentence naming every working and uploaded Word document.</returns>
    public string DescribeAvailable(WordWorkspaceState state)
    {
        var parts = new List<string>();

        if (state.Documents.Count > 0)
        {
            parts.Add("Working documents: " + string.Join(", ", state.Documents.Select(document => $"\"{document.Name}\"")));
        }

        var uploads = WordUploads.ToList();

        if (uploads.Count > 0)
        {
            parts.Add("Uploaded Word documents: " + string.Join(", ", uploads.Select(document => $"\"{document.FileName}\"")));
        }

        return parts.Count == 0
            ? "No Word documents are available."
            : string.Join(". ", parts) + ".";
    }

    /// <summary>
    /// Reads the file of a document.
    /// </summary>
    /// <param name="source">The document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <c>.docx</c> file.</returns>
    public async Task<byte[]> ReadBytesAsync(WordSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.IsUpload)
        {
            return await ReadUploadAsync(source.Upload, cancellationToken)
                ?? throw new WordToolException($"The stored file for \"{source.Name}\" is missing. Ask the user to upload it again.");
        }

        var path = source.Working.BlobPath;

        for (var attempt = 0; ; attempt++)
        {
            var bytes = await _workspaceStore.ReadBlobAsync(path, cancellationToken);

            if (bytes is not null)
            {
                return bytes;
            }

            // A reading call does not take the workspace's lock, so an edit saved by a call running alongside it
            // may have replaced the version it found, and deleted that version's file. The workspace is read
            // again for the current one. Under the lock — or with nothing newer — the file is really gone.
            if (_mutation is not null || Scope is null || attempt >= 2)
            {
                break;
            }

            var current = (await _workspaceStore.LoadAsync(Scope.Value, cancellationToken)).Find(source.Working.Name);

            if (current is null || string.Equals(current.BlobPath, path, StringComparison.Ordinal))
            {
                break;
            }

            path = current.BlobPath;
        }

        throw new WordToolException($"The file behind working document \"{source.Name}\" is missing. Recreate it, or import the upload it came from again.");
    }

    /// <summary>
    /// Reads the stored bytes of an uploaded file of this conversation.
    /// </summary>
    /// <param name="document">The upload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bytes, or <see langword="null"/> when the file is missing or not this conversation's.</returns>
    public async Task<byte[]> ReadUploadAsync(AIDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.StoredFilePath) || !_readableReferences.Contains(document.ReferenceId ?? string.Empty))
        {
            return null;
        }

        if (document.FileSize > Options.MaxDocumentBytes)
        {
            throw new WordToolException($"\"{document.FileName}\" is {document.FileSize:N0} bytes, larger than the {Options.MaxDocumentBytes:N0} bytes this agent opens.");
        }

        var bytes = await ReadStoredFileAsync(document.StoredFilePath, cancellationToken);

        // Every Word upload is opened through here — to read or edit it, import it, start from it as a template
        // or compare it — so this is where one built to expand into far more than its size is stopped.
        if (bytes is not null && WordPackage.IsWordFile(document.FileName))
        {
            if (bytes.LongLength > Options.MaxDocumentBytes)
            {
                throw new WordToolException($"\"{document.FileName}\" is {bytes.LongLength:N0} bytes, larger than the {Options.MaxDocumentBytes:N0} bytes this agent opens.");
            }

            WordPackageGuard.EnsureSafe(bytes, $"\"{document.FileName}\"", Options.MaxUncompressedDocumentBytes);
        }

        return bytes;
    }

    /// <summary>
    /// Adds a new working document to the workspace and makes it the active one.
    /// </summary>
    /// <param name="state">The workspace, inside <see cref="MutateAsync"/>.</param>
    /// <param name="name">The name asked for; a number is added when it is taken.</param>
    /// <param name="bytes">The <c>.docx</c> file.</param>
    /// <param name="design">The design the document was built with, or <see langword="null"/>.</param>
    /// <param name="change">What was done, for the document's history.</param>
    /// <param name="sourceUpload">The upload the document was made from, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The working document.</returns>
    public async Task<WordWorkingDocument> AddDocumentAsync(
        WordWorkspaceState state,
        string name,
        byte[] bytes,
        WordDesign design,
        string change,
        AIDocument sourceUpload = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(bytes);

        if (state.Documents.Count >= Options.MaxWorkingDocuments)
        {
            throw new WordToolException($"This conversation already holds {state.Documents.Count} working documents, the most it keeps. Reuse one, or delete one with remove_word_content (scope 'document').");
        }

        EnsureSize(bytes);

        var now = TimeProvider.GetUtcNow().UtcDateTime;
        var document = new WordWorkingDocument
        {
            Name = UniqueName(state, name),
            BlobPath = await WriteBlobAsync(bytes, cancellationToken),
            ByteLength = bytes.LongLength,
            Version = 1,
            Design = design,
            SourceFileName = sourceUpload?.FileName,
            SourceDocumentId = sourceUpload?.ItemId,
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        document.Record(change);
        state.Documents.Add(document);
        state.ActiveDocument = document.Name;

        return document;
    }

    /// <summary>
    /// Removes a working document and its file from the workspace. Inside <see cref="MutateAsync"/>, the file is
    /// deleted once the workspace is saved.
    /// </summary>
    /// <param name="state">The workspace, inside <see cref="MutateAsync"/>.</param>
    /// <param name="document">The document.</param>
    public async Task RemoveDocumentAsync(WordWorkspaceState state, WordWorkingDocument document)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(document);

        state.Documents.Remove(document);
        await DeleteBlobAfterSaveAsync(document.BlobPath);

        if (string.Equals(state.ActiveDocument, document.Name, StringComparison.OrdinalIgnoreCase))
        {
            state.ActiveDocument = state.Documents.LastOrDefault()?.Name;
        }
    }

    /// <summary>
    /// Saves an edited file as a new version of a working document — never over an upload. The first edit of
    /// an upload saves a working copy named after it, and later edits change that copy.
    /// </summary>
    /// <param name="state">The workspace, inside <see cref="MutateAsync"/>.</param>
    /// <param name="target">The document that was edited.</param>
    /// <param name="saveAs">The name asked for, or <see langword="null"/>.</param>
    /// <param name="bytes">The edited file.</param>
    /// <param name="change">What was done, for the document's history.</param>
    /// <param name="design">A new design to remember, or <see langword="null"/> to keep the document's.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The working document the file was saved as.</returns>
    public async Task<WordWorkingDocument> SaveWorkingAsync(
        WordWorkspaceState state,
        WordSource target,
        string saveAs,
        byte[] bytes,
        string change,
        WordDesign design = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(bytes);

        EnsureSize(bytes);

        var name = !string.IsNullOrWhiteSpace(saveAs)
            ? SanitizeName(saveAs)
            : target.Working?.Name ?? SanitizeName(Path.GetFileNameWithoutExtension(target.Name));

        var existing = state.Find(name);

        if (existing is not null && !IsSameDocument(existing, target, saveAs))
        {
            // Only the document that was edited is changed in place. Any other working document of that name —
            // one 'save_as' names, or one made from something else — is kept, and the result gets a new name.
            var requested = name;

            name = UniqueName(state, name);
            existing = null;

            if (!string.IsNullOrWhiteSpace(saveAs))
            {
                Notes.Add($"A working document named \"{requested}\" already exists and was left unchanged; the result was saved as \"{name}\".");
            }
        }

        if (existing is null)
        {
            var origin = target.IsUpload ? target.Upload : null;
            var created = await AddDocumentAsync(state, name, bytes, design ?? target.Working?.Design, change, origin, cancellationToken);

            if (target.Working is not null)
            {
                created.SourceDocumentId = target.Working.SourceDocumentId;
                created.SourceFileName = target.Working.SourceFileName;
                created.History.InsertRange(0, target.Working.History);
            }

            return created;
        }

        var previousBlob = existing.BlobPath;

        existing.BlobPath = await WriteBlobAsync(bytes, cancellationToken);
        existing.ByteLength = bytes.LongLength;
        existing.Version++;
        existing.UpdatedUtc = TimeProvider.GetUtcNow().UtcDateTime;

        if (design is not null)
        {
            existing.Design = design;
        }

        existing.Record(change);

        if (!string.IsNullOrEmpty(previousBlob) && !string.Equals(previousBlob, existing.BlobPath, StringComparison.Ordinal))
        {
            await DeleteBlobAfterSaveAsync(previousBlob);
        }

        state.ActiveDocument = existing.Name;

        return existing;
    }

    /// <summary>
    /// Returns whether a working document found under the name a result is saved as is the document that was
    /// edited, so the result replaces it rather than being saved under a new name.
    /// </summary>
    /// <param name="existing">The working document found under the name.</param>
    /// <param name="target">The document that was edited.</param>
    /// <param name="saveAs">The name asked for, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the result is a new version of <paramref name="existing"/>.</returns>
    private static bool IsSameDocument(WordWorkingDocument existing, WordSource target, string saveAs)
    {
        if (target.Working is not null)
        {
            return string.Equals(existing.Name, target.Working.Name, StringComparison.OrdinalIgnoreCase);
        }

        // The first edit of an upload saves a copy named after it, and a later edit of the upload without a new
        // name changes that copy.
        return string.IsNullOrWhiteSpace(saveAs) &&
            string.Equals(existing.SourceDocumentId, target.Upload.ItemId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Makes a name unique within the workspace by adding a number.
    /// </summary>
    /// <param name="state">The workspace.</param>
    /// <param name="name">The name.</param>
    /// <returns>The name, or the name with the first free number appended.</returns>
    public static string UniqueName(WordWorkspaceState state, string name)
    {
        ArgumentNullException.ThrowIfNull(state);

        var candidate = SanitizeName(name);

        if (state.Find(candidate) is null)
        {
            return candidate;
        }

        for (var number = 2; ; number++)
        {
            var numbered = candidate + "-" + number.ToString(CultureInfo.InvariantCulture);

            if (state.Find(numbered) is null)
            {
                return numbered;
            }
        }
    }

    /// <summary>
    /// Cleans a name for use as a working document name and a file name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The cleaned name.</returns>
    public static string SanitizeName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();

        if (WordPackage.IsWordFile(trimmed))
        {
            trimmed = Path.GetFileNameWithoutExtension(trimmed);
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
    /// Stores a file as a download attached to the conversation.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="bytes">The file.</param>
    /// <param name="contentType">The media type it is served with, or <see langword="null"/> to infer it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <c>[doc:N]</c> marker that links it.</returns>
    public async Task<string> ExportAsync(string fileName, byte[] bytes, string contentType = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var scope = RequireScope();

        if (_generatedDocuments is null)
        {
            throw new WordToolException("Downloads are not available on this host.");
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
    public async Task<List<string>> ShowFiguresAsync(IReadOnlyList<WordFigure> figures, CancellationToken cancellationToken = default)
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
        var stored = new List<string>(figures.Count);

        try
        {
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

                stored.Add(result.Document.ItemId);

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
        }
        finally
        {
            await RememberPreviewDocumentsAsync(stored);
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
    /// Remembers the pictures a preview stored in a chat interaction, so clearing its history deletes them.
    /// </summary>
    /// <remarks>
    /// A preview is not a download: its reference is not marked as generated, so the cleanup that deletes the
    /// generated files of cleared messages does not see it. A chat session needs no record, since its pictures
    /// are deleted with the session.
    /// </remarks>
    /// <param name="documentIds">The stored pictures.</param>
    private async Task RememberPreviewDocumentsAsync(List<string> documentIds)
    {
        if (documentIds.Count == 0 ||
            Scope is not { } scope ||
            !string.Equals(scope.ReferenceType, AIReferenceTypes.Document.ChatInteraction, StringComparison.Ordinal))
        {
            return;
        }

        if (_mutation is not null)
        {
            _mutation.State.PreviewDocumentIds.AddRange(documentIds);

            return;
        }

        try
        {
            // Not cancelled: the pictures are already stored, and are shown either way.
            await MutateAsync(state =>
            {
                state.PreviewDocumentIds.AddRange(documentIds);

                return Task.FromResult(true);
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "The Word workspace could not record {Count} preview picture(s); clearing the history will not delete them.", documentIds.Count);
        }
    }

    /// <summary>
    /// Builds the address the host serves a document from.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <returns>The link, or <see langword="null"/> when the host exposes no download endpoint.</returns>
    public string ResolveDownloadLink(string documentId)
    {
        var linkGenerator = Services.GetService<LinkGenerator>();

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
            var httpContext = Services.GetService<IHttpContextAccessor>()?.HttpContext;

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
    /// Resolves a picture a tool call refers to, from this conversation only: an uploaded image, a <c>[fig:N]</c>
    /// marker shown in this turn, a <c>figure:{documentId}/{figureId}</c> reference, a link this host serves, or a
    /// <c>data:</c> address.
    /// </summary>
    /// <param name="source">The picture source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The picture, or <see langword="null"/> when it cannot be found or is not a picture.</returns>
    public async Task<WordImageData> ResolveImageAsync(string source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        try
        {
            var bytes = await ResolveImageBytesAsync(source.Trim(), cancellationToken);

            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }

            if (bytes.Length > Options.MaxImageBytes)
            {
                throw new WordToolException($"The picture \"{source}\" is {bytes.Length:N0} bytes, larger than the {Options.MaxImageBytes:N0} bytes a document places.");
            }

            return WordImageInfo.TryRead(bytes, out var info)
                ? new WordImageData(bytes, info, source)
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not WordToolException)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(ex, "The Word image source '{Source}' could not be resolved.", source);
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

        var upload = FindUpload(source, wordOnly: false);

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

    private async Task<string> WriteBlobAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        var path = await _workspaceStore.WriteBlobAsync(RequireScope(), bytes, ".docx", cancellationToken);

        _mutation?.Written.Add(path);

        return path;
    }

    private async Task DeleteBlobAfterSaveAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (_mutation is null)
        {
            await _workspaceStore.DeleteBlobAsync(path);

            return;
        }

        // A file written by this same change is not referred to by the stored workspace, so it can go now.
        if (_mutation.Written.Remove(path))
        {
            await _workspaceStore.DeleteBlobAsync(path);

            return;
        }

        _mutation.Replaced.Add(path);
    }

    private async Task DeleteReplacedBlobsAsync(WordWorkspaceScope scope, WordWorkspaceState state, List<string> paths)
    {
        var leftBehind = new List<string>();

        foreach (var path in paths)
        {
            if (!await _workspaceStore.DeleteBlobAsync(path))
            {
                leftBehind.Add(path);
            }
        }

        if (leftBehind.Count == 0)
        {
            return;
        }

        // Remembered, so deleting the workspace deletes them too. The change itself is already saved, so a
        // failure here only costs storage.
        state.OrphanedBlobs.AddRange(leftBehind.Where(path => !state.OrphanedBlobs.Contains(path, StringComparer.Ordinal)));

        try
        {
            await _workspaceStore.SaveAsync(scope, state, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "The Word workspace could not record {Count} file(s) it failed to delete.", leftBehind.Count);
        }
    }

    private void EnsureSize(byte[] bytes)
    {
        if (bytes.LongLength > Options.MaxDocumentBytes)
        {
            throw new WordToolException($"The result is {bytes.LongLength:N0} bytes, larger than the {Options.MaxDocumentBytes:N0} bytes this agent keeps.");
        }

        WordPackageGuard.EnsureSafe(bytes, "The result", Options.MaxUncompressedDocumentBytes);
    }

    private static WordPackage OpenPackage(byte[] bytes, WordSource source)
    {
        try
        {
            return WordPackage.Open(bytes);
        }
        catch (InvalidDataException ex)
        {
            throw new WordToolException($"\"{source.Name}\" cannot be opened as a Word document. {ex.Message}", ex);
        }
    }

    /// <summary>
    /// The stored files a change in progress wrote and replaced, so the replaced ones are deleted only once the
    /// change is saved, and the written ones when it is abandoned.
    /// </summary>
    private sealed class Mutation
    {
        public Mutation(WordWorkspaceState state)
        {
            State = state;
        }

        public WordWorkspaceState State { get; }

        public List<string> Written { get; } = [];

        public List<string> Replaced { get; } = [];
    }
}
