using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;

namespace CrestApps.Core.AI.Chat.Services;

/// <summary>
/// Collects chat citation references from orchestration and tool execution context,
/// resolves any configured links, and records article content item IDs for hosts
/// that need follow-up lookups.
/// </summary>
public sealed class CitationReferenceCollector
{
    private const string DataSourceReferencesKey = "DataSourceReferences";
    private const string DocumentReferencesKey = "DocumentReferences";

    private readonly CompositeAIReferenceLinkResolver _linkResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="CitationReferenceCollector"/> class.
    /// </summary>
    /// <param name="linkResolver">The composite link resolver.</param>
    public CitationReferenceCollector(CompositeAIReferenceLinkResolver linkResolver)
    {
        _linkResolver = linkResolver;
    }

    /// <summary>
    /// Collects citation references stored on the orchestration context and resolves
    /// any configured links.
    /// </summary>
    /// <param name="orchestrationContext">The orchestration context.</param>
    /// <param name="references">The target citation map.</param>
    /// <param name="contentItemIds">The target article content item ID set.</param>
    public void CollectPreemptiveReferences(
        OrchestrationContext orchestrationContext,
        Dictionary<string, AICompletionReference> references,
        HashSet<string> contentItemIds)
    {
        ArgumentNullException.ThrowIfNull(orchestrationContext);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(contentItemIds);

        CollectFromProperties(orchestrationContext, DataSourceReferencesKey, references);
        CollectFromProperties(orchestrationContext, DocumentReferencesKey, references);
        ResolveLinks(references, contentItemIds);
    }

    /// <summary>
    /// Collects citation references captured during tool execution and resolves any
    /// configured links.
    /// </summary>
    /// <param name="references">The target citation map.</param>
    /// <param name="contentItemIds">The target article content item ID set.</param>
    /// <returns><see langword="true"/> when new references were added; otherwise, <see langword="false"/>.</returns>
    public bool CollectToolReferences(
        Dictionary<string, AICompletionReference> references,
        HashSet<string> contentItemIds)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(contentItemIds);

        var invocationContext = AIInvocationScope.Current;

        if (invocationContext is null)
        {
            return false;
        }

        var added = false;

        foreach (var (key, value) in invocationContext.ToolReferences)
        {
            if (references.TryAdd(key, value))
            {
                added = true;
            }
        }

        if (added)
        {
            ResolveLinks(references, contentItemIds);
        }

        return added;
    }

    private void ResolveLinks(
        Dictionary<string, AICompletionReference> references,
        HashSet<string> contentItemIds)
    {
        foreach (var (_, reference) in references)
        {
            if (string.IsNullOrEmpty(reference.Link) &&
                !string.IsNullOrEmpty(reference.ReferenceId) &&
                !string.IsNullOrEmpty(reference.ReferenceType))
            {
                reference.Link = _linkResolver.ResolveLink(
                    reference.ReferenceId,
                    reference.ReferenceType,
                    new Dictionary<string, object>
                    {
                        ["Title"] = reference.Title,
                    });
            }

            if (!string.IsNullOrEmpty(reference.ReferenceId) &&
                string.Equals(reference.ReferenceType, IndexProfileTypes.Articles, StringComparison.OrdinalIgnoreCase))
            {
                contentItemIds.Add(reference.ReferenceId);
            }
        }
    }

    private static void CollectFromProperties(
        OrchestrationContext orchestrationContext,
        string propertyKey,
        Dictionary<string, AICompletionReference> target)
    {
        if (orchestrationContext.Properties.TryGetValue(propertyKey, out var refsObj) &&
            refsObj is Dictionary<string, AICompletionReference> refs)
        {
            foreach (var (key, value) in refs)
            {
                target.TryAdd(key, value);
            }
        }
    }
}
