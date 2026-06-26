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

    private TabularToolContext(
        IReadOnlyList<TabularDocumentRef> documents,
        IAIDocumentChunkStore chunkStore)
    {
        Documents = documents;
        _chunkStore = chunkStore;
    }

    /// <summary>
    /// Gets the tabular documents available to the active conversation.
    /// </summary>
    public IReadOnlyList<TabularDocumentRef> Documents { get; }

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
        var first = true;

        foreach (var chunk in chunks.OrderBy(c => c.Index))
        {
            if (!first)
            {
                builder.Append('\n');
            }

            builder.Append(chunk.Content);
            first = false;
        }

        return builder.ToString();
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

        if (documentStore is null || chunkStore is null)
        {
            return null;
        }

        var documentOptions = services.GetRequiredService<IOptions<ChatDocumentsOptions>>().Value;
        var session = ResolveSession();
        var scopes = new List<(string ReferenceId, string ReferenceType)>();

        switch (executionContext.Resource)
        {
            case ChatInteraction interaction:
                scopes.Add((interaction.ItemId, AIReferenceTypes.Document.ChatInteraction));
                break;

            case AIProfile profile:
                scopes.Add((profile.ItemId, AIReferenceTypes.Document.Profile));

                if (session is not null && !string.IsNullOrEmpty(session.SessionId))
                {
                    scopes.Add((session.SessionId, AIReferenceTypes.Document.ChatSession));
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
                if (documentOptions.IsTabularFileExtension(document.FileName) && seen.Add(document.ItemId))
                {
                    documents.Add(new TabularDocumentRef(document.ItemId, document.FileName));
                }
            }
        }

        return new TabularToolContext(documents, chunkStore);
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
