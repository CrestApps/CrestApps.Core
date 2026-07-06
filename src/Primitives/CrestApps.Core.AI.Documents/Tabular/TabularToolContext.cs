using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Cysharp.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Resolves the tabular documents for the active AI invocation and loads the reconstructed text
/// content for a document on demand. Shared by the tabular AI tools so they all scope to the same
/// conversation and never reach documents from another session.
/// </summary>
internal sealed class TabularToolContext
{
    private readonly IAIDocumentStore _documentStore;
    private readonly IAIDocumentChunkStore _chunkStore;
    private readonly ITabularDocumentArtifactStore _artifactStore;
    private readonly TabularDocumentArtifactFactory _artifactFactory;

    private TabularToolContext(
        IReadOnlyList<TabularDocumentRef> documents,
        IAIDocumentStore documentStore,
        IAIDocumentChunkStore chunkStore,
        ITabularDocumentArtifactStore artifactStore,
        TabularDocumentArtifactFactory artifactFactory,
        string databasePath,
        string exportReferenceId,
        string exportReferenceType)
    {
        Documents = documents;
        _documentStore = documentStore;
        _chunkStore = chunkStore;
        _artifactStore = artifactStore;
        _artifactFactory = artifactFactory;
        DatabasePath = databasePath;
        ExportReferenceId = exportReferenceId;
        ExportReferenceType = exportReferenceType;
    }

    /// <summary>
    /// Gets the tabular documents available to the active conversation.
    /// </summary>
    public IReadOnlyList<TabularDocumentRef> Documents { get; }

    /// <summary>
    /// Gets the absolute path to the file-backed SQLite database for the workspace scope.
    /// May be <see langword="null"/> when no durable path could be resolved (for example during tests).
    /// </summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Gets the reference id that generated tabular files should be attached to for download.
    /// </summary>
    public string ExportReferenceId { get; }

    /// <summary>
    /// Gets the reference type that generated tabular files should be attached to for download.
    /// </summary>
    public string ExportReferenceType { get; }

    /// <summary>
    /// Loads the reconstructed text content of a document from its stored chunks.
    /// </summary>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document content, or an empty string when no content is available.</returns>
    public async Task<string> LoadContentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var chunks = await _chunkStore.GetChunksByAIDocumentIdAsync(documentId);

        if (chunks.Count == 0)
        {
            return string.Empty;
        }

        using var builder = ZString.CreateStringBuilder();
        foreach (var chunk in chunks.OrderBy(c => c.Index))
        {
            builder.Append(chunk.Content);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Loads the parsed artifact for a document, creating and storing it durably when it is missing.
    /// </summary>
    /// <param name="document">The tabular document reference.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed tabular artifact.</returns>
    public async Task<TabularDocumentArtifact> LoadArtifactAsync(TabularDocumentRef document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var artifact = await _artifactStore.GetAsync(document.DocumentId, cancellationToken);

        if (artifact is not null)
        {
            return artifact;
        }

        var storedDocument = await _documentStore.FindByIdAsync(document.DocumentId, cancellationToken);
        artifact = storedDocument == null ? null : await _artifactFactory.CreateAsync(storedDocument, cancellationToken);

        if (artifact != null)
        {
            await _artifactStore.SaveAsync(document.DocumentId, artifact, cancellationToken);

            return artifact;
        }

        var content = await LoadContentAsync(document.DocumentId, cancellationToken);
        artifact = TabularDocumentArtifact.FromDelimitedContent(content, document.FileName);

        await _artifactStore.SaveAsync(document.DocumentId, artifact, cancellationToken);

        return artifact;
    }

    public async Task<TabularWorkspaceImportResult> ImportToWorkspaceAsync(
        TabularDocumentRef document,
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrEmpty(tableName);

        var storedDocument = await _documentStore.FindByIdAsync(document.DocumentId, cancellationToken);

        if (storedDocument == null)
        {
            return null;
        }

        return await _artifactFactory.ImportToWorkspaceAsync(
            storedDocument,
            connection,
            tableName,
            cancellationToken);
    }

    /// <summary>
    /// Resolves the tabular tool context from the current <see cref="AIInvocationScope"/>.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved context, or <see langword="null"/> when no conversation scope is available.</returns>
    public static async Task<TabularToolContext> ResolveAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var invocationContext = AIInvocationScope.Current;
        var executionContext = invocationContext?.ToolExecutionContext;

        var documentStore = services.GetService<IAIDocumentStore>();
        var chunkStore = services.GetService<IAIDocumentChunkStore>();
        var artifactStore = services.GetService<ITabularDocumentArtifactStore>();
        var artifactFactory = services.GetService<TabularDocumentArtifactFactory>();

        if (documentStore is null || chunkStore is null || artifactStore is null || artifactFactory is null)
        {
            return null;
        }

        var documentOptions = services.GetRequiredService<IOptions<ChatDocumentsOptions>>().Value;
        var session = ResolveSession();
        var scopes = new List<(string ReferenceId, string ReferenceType)>();
        string exportReferenceId = null;
        string exportReferenceType = null;

        if (executionContext is null)
        {
            return null;
        }

        switch (executionContext.Resource)
        {
            case ChatInteraction interaction:
                scopes.Add((interaction.ItemId, AIReferenceTypes.Document.ChatInteraction));
                exportReferenceId = interaction.ItemId;
                exportReferenceType = AIReferenceTypes.Document.ChatInteraction;
                break;

            case AIProfile profile:
                scopes.Add((profile.ItemId, AIReferenceTypes.Document.Profile));

                if (session is not null && !string.IsNullOrEmpty(session.SessionId))
                {
                    scopes.Add((session.SessionId, AIReferenceTypes.Document.ChatSession));
                    exportReferenceId = session.SessionId;
                    exportReferenceType = AIReferenceTypes.Document.ChatSession;
                }

                break;

            default:
                return null;
        }

        var documents = new List<TabularDocumentRef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (referenceId, referenceType) in scopes)
        {
            if (string.IsNullOrEmpty(referenceId))
            {
                continue;
            }

            var found = await documentStore.GetDocumentsAsync(referenceId, referenceType);

            foreach (var document in found)
            {
                // Skip files this conversation generated (for example a tabular export). They are
                // outputs, not sources, so they must never be re-ingested as duplicate workspace tables.
                if (document.Get<bool>(DefaultGeneratedDocumentService.GeneratedPropertyName))
                {
                    continue;
                }

                if (documentOptions.IsTabularFileExtension(document.FileName) && seen.Add(document.ItemId))
                {
                    documents.Add(new TabularDocumentRef(document.ItemId, document.FileName));
                }
            }
        }

        var databasePath = ResolveDatabasePath(services, exportReferenceType, exportReferenceId);

        return new TabularToolContext(
            documents,
            documentStore,
            chunkStore,
            artifactStore,
            artifactFactory,
            databasePath,
            exportReferenceId,
            exportReferenceType);
    }

    private static string ResolveDatabasePath(IServiceProvider services, string referenceType, string referenceId)
    {
        if (string.IsNullOrEmpty(referenceType) || string.IsNullOrEmpty(referenceId))
        {
            return null;
        }

        var fileStoreOptions = services.GetRequiredService<IOptions<DocumentFileSystemFileStoreOptions>>().Value;

        if (string.IsNullOrEmpty(fileStoreOptions.BasePath))
        {
            return null;
        }

        return Path.Combine(fileStoreOptions.BasePath, "documents", referenceType, referenceId, "data", "tabular.db");
    }

    private static AIChatSession ResolveSession()
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null)
        {
            return null;
        }

        if (invocationContext.ChatSession is not null)
        {
            return invocationContext.ChatSession;
        }

        if (invocationContext.Items.TryGetValue(nameof(AIChatSession), out var value) && value is AIChatSession session)
        {
            return session;
        }

        return null;
    }
}
