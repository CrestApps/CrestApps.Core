using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Cysharp.Text;
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
    private readonly IAIDocumentChunkStore _chunkStore;
    private readonly ITabularDocumentArtifactStore _artifactStore;

    private TabularToolContext(
        IReadOnlyList<TabularDocumentRef> documents,
        IAIDocumentChunkStore chunkStore,
        ITabularDocumentArtifactStore artifactStore,
        TabularWorkspaceCacheKey cacheKey,
        string exportReferenceId,
        string exportReferenceType)
    {
        Documents = documents;
        _chunkStore = chunkStore;
        _artifactStore = artifactStore;
        CacheKey = cacheKey;
        ExportReferenceId = exportReferenceId;
        ExportReferenceType = exportReferenceType;
    }

    /// <summary>
    /// Gets the tabular documents available to the active conversation.
    /// </summary>
    public IReadOnlyList<TabularDocumentRef> Documents { get; }

    /// <summary>
    /// Gets the cache key used to reuse the tabular workspace across active prompts in the same scope.
    /// </summary>
    public TabularWorkspaceCacheKey CacheKey { get; }

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

        var content = await LoadContentAsync(document.DocumentId, cancellationToken);
        artifact = TabularDocumentArtifact.FromDelimitedContent(content, document.FileName);
        await _artifactStore.SaveAsync(document.DocumentId, artifact, cancellationToken);

        return artifact;
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

        if (executionContext is null)
        {
            return null;
        }

        var documentStore = services.GetService<IAIDocumentStore>();
        var chunkStore = services.GetService<IAIDocumentChunkStore>();
        var artifactStore = services.GetService<ITabularDocumentArtifactStore>();

        if (documentStore is null || chunkStore is null || artifactStore is null)
        {
            return null;
        }

        var documentOptions = services.GetRequiredService<IOptions<ChatDocumentsOptions>>().Value;
        var session = ResolveSession();
        var scopes = new List<(string ReferenceId, string ReferenceType)>();
        string chatInteractionId = null;
        string chatSessionId = null;
        string profileId = null;
        string exportReferenceId = null;
        string exportReferenceType = null;

        switch (executionContext.Resource)
        {
            case ChatInteraction interaction:
                scopes.Add((interaction.ItemId, AIReferenceTypes.Document.ChatInteraction));
                chatInteractionId = interaction.ItemId;
                exportReferenceId = interaction.ItemId;
                exportReferenceType = AIReferenceTypes.Document.ChatInteraction;
                break;

            case AIProfile profile:
                scopes.Add((profile.ItemId, AIReferenceTypes.Document.Profile));
                profileId = profile.ItemId;

                if (session is not null && !string.IsNullOrEmpty(session.SessionId))
                {
                    scopes.Add((session.SessionId, AIReferenceTypes.Document.ChatSession));
                    chatSessionId = session.SessionId;
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

        var cacheKey = BuildCacheKey(documents, scopes, chatInteractionId, chatSessionId, profileId);

        return new TabularToolContext(
            documents,
            chunkStore,
            artifactStore,
            cacheKey,
            exportReferenceId,
            exportReferenceType);
    }

    private static TabularWorkspaceCacheKey BuildCacheKey(
        IReadOnlyList<TabularDocumentRef> documents,
        IReadOnlyList<(string ReferenceId, string ReferenceType)> scopes,
        string chatInteractionId,
        string chatSessionId,
        string profileId)
    {
        var references = scopes
            .Where(scope => !string.IsNullOrEmpty(scope.ReferenceId) && !string.IsNullOrEmpty(scope.ReferenceType))
            .Select(scope => (scope.ReferenceType, scope.ReferenceId))
            .ToArray();
        var scopePart = string.Join('|', references
            .OrderBy(reference => reference.ReferenceType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.ReferenceId, StringComparer.OrdinalIgnoreCase)
            .Select(reference => $"{reference.ReferenceType}:{reference.ReferenceId}"));
        var documentPart = string.Join('|', documents
            .OrderBy(document => document.DocumentId, StringComparer.OrdinalIgnoreCase)
            .Select(document => $"{document.DocumentId}:{document.FileName}"));
        var key = $"{scopePart}::documents:{documentPart}";

        return new TabularWorkspaceCacheKey(key, chatInteractionId, chatSessionId, profileId, references);
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
