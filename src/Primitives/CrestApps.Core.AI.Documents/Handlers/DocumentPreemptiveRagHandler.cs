using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Templates.Services;
using Cysharp.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Handlers;

/// <summary>
/// Preemptively retrieves relevant document context for uploaded documents or profile knowledge documents
/// and injects it into the orchestration system message before model generation begins.
/// </summary>
internal sealed class DocumentPreemptiveRagHandler : IPreemptiveRagHandler
{
    private readonly IAIClientFactory _aiClientFactory;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly ISearchIndexProfileStore _indexProfileStore;
    private readonly ITemplateService _templateService;
    private readonly IAITextNormalizer _textNormalizer;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentPreemptiveRagHandler> _logger;

    public DocumentPreemptiveRagHandler(
        IAIClientFactory aiClientFactory,
        IAIDeploymentManager deploymentManager,
        ISearchIndexProfileStore indexProfileStore,
        ITemplateService templateService,
        IAITextNormalizer textNormalizer,
        IServiceProvider serviceProvider,
        ILogger<DocumentPreemptiveRagHandler> logger)
    {
        _aiClientFactory = aiClientFactory;
        _deploymentManager = deploymentManager;
        _indexProfileStore = indexProfileStore;
        _templateService = templateService;
        _textNormalizer = textNormalizer;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public ValueTask<bool> CanHandleAsync(OrchestrationContextBuiltContext context)
    {
        if (context.OrchestrationContext.Documents is { Count: > 0 })
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("DocumentPreemptiveRagHandler can handle: {DocCount} document(s) found in orchestration context.", context.OrchestrationContext.Documents.Count);
            }

            return ValueTask.FromResult(true);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("DocumentPreemptiveRagHandler skipped: no documents in orchestration context.");
        }

        return ValueTask.FromResult(false);
    }

    public async Task HandleAsync(PreemptiveRagContext context)
    {
        var snapshotSettings = _serviceProvider.GetService<IOptionsSnapshot<InteractionDocumentOptions>>()?.Value;
        var optionsSettings = _serviceProvider.GetRequiredService<IOptions<InteractionDocumentOptions>>().Value;
        var defaultSettings = !string.IsNullOrWhiteSpace(snapshotSettings?.IndexProfileName)
            ? snapshotSettings
            : optionsSettings;
        var userSuppliedDocuments = DocumentContextInjectionModeResolver.ResolveUserSuppliedDocuments(context);
        var fullUserDocumentMode = DocumentContextInjectionModeResolver.Resolve(context.OrchestrationContext, userSuppliedDocuments.Count);

        if (string.IsNullOrEmpty(defaultSettings.IndexProfileName) &&
            fullUserDocumentMode != DocumentContextInjectionMode.FullUserDocuments)
        {
            return;
        }

        try
        {
            await InjectPreemptiveRagContextAsync(context, ResolveSettings(context.Resource, defaultSettings));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during document Preemptive RAG injection.");
        }
    }

    private async Task InjectPreemptiveRagContextAsync(PreemptiveRagContext context, InteractionDocumentOptions settings)
    {
        var searchScopes = ResolveSearchScopes(context);

        if (searchScopes.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Document Preemptive RAG: no search scopes resolved for resource type '{ResourceType}'.", context.Resource?.GetType().Name);
            }

            return;
        }

        var showUserDocumentAwareness =
            context.Resource is not AIProfile ||
            searchScopes.Any(scope => scope.ReferenceType == AIReferenceTypes.Document.ChatSession);

        var topN = settings.TopN > 0 ? settings.TopN : 3;
        var hasProfileScope = searchScopes.Any(scope => scope.ReferenceType == AIReferenceTypes.Document.Profile);
        var hasSessionScope = searchScopes.Any(scope => scope.ReferenceType == AIReferenceTypes.Document.ChatSession);
        var keepProfileDocumentAwareness = !(context.Resource is AIProfile && hasProfileScope && hasSessionScope);
        var userSuppliedDocuments = DocumentContextInjectionModeResolver.ResolveUserSuppliedDocuments(context);
        var fullUserDocumentMode = DocumentContextInjectionModeResolver.Resolve(context.OrchestrationContext, userSuppliedDocuments.Count);
        var userDocumentContext = string.Empty;
        var invocationContext = AIInvocationScope.Current;
        var seenDocuments = new Dictionary<string, (int Index, string FileName)>(StringComparer.OrdinalIgnoreCase);

        if (fullUserDocumentMode == DocumentContextInjectionMode.FullUserDocuments)
        {
            userDocumentContext = await AppendFullUserDocumentContextAsync(userSuppliedDocuments, invocationContext, seenDocuments);
            searchScopes = searchScopes
                .Where(scope =>
                    scope.ReferenceType != AIReferenceTypes.Document.ChatInteraction &&
                    scope.ReferenceType != AIReferenceTypes.Document.ChatSession)
                .ToList();
        }

        var finalResults = await SearchRelevantChunksAsync(context, settings, searchScopes, topN);

        if (string.IsNullOrEmpty(userDocumentContext) && finalResults.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Document Preemptive RAG: no relevant document context found for the current request.");
            }

            return;
        }

        var orchestrationContext = context.OrchestrationContext;

        using var builder = ZString.CreateStringBuilder();

        var arguments = new Dictionary<string, object>();
        var hasUserSuppliedDocumentContext = !string.IsNullOrEmpty(userDocumentContext) || finalResults.Any(x =>
            x.ReferenceType == AIReferenceTypes.Document.ChatInteraction ||
            x.ReferenceType == AIReferenceTypes.Document.ChatSession);
        var hasKnowledgeBaseDocumentContext = finalResults.Any(x => x.ReferenceType == AIReferenceTypes.Document.Profile);

        if (!orchestrationContext.DisableTools)
        {
            arguments["searchToolName"] = SystemToolNames.SearchDocuments;
        }

        arguments["hasUserSuppliedDocumentContext"] = hasUserSuppliedDocumentContext;
        arguments["hasKnowledgeBaseDocumentContext"] = hasKnowledgeBaseDocumentContext;
        arguments["hasFullUserDocumentContext"] = !string.IsNullOrEmpty(userDocumentContext);

        var header = await _templateService.RenderAsync(AITemplateIds.DocumentContextHeader, arguments);

        if (!string.IsNullOrEmpty(header))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append(header);
        }

        if (!string.IsNullOrEmpty(userDocumentContext))
        {
            builder.Append(userDocumentContext);
        }

        if (finalResults.Count > 0)
        {
            if (settings.RetrievalMode == DocumentRetrievalMode.Hierarchical)
            {
                builder.Append(await AppendHierarchicalContextAsync(finalResults, topN, showUserDocumentAwareness, keepProfileDocumentAwareness, invocationContext, seenDocuments));
            }
            else if (showUserDocumentAwareness)
            {
                builder.Append(AppendChunkContext(finalResults.Take(topN), keepProfileDocumentAwareness, invocationContext, seenDocuments));
            }
            else
            {
                foreach (var (result, _) in finalResults.Take(topN))
                {
                    builder.AppendLine("---");
                    builder.AppendLine(result.Chunk.Text);
                }
            }
        }

        if (showUserDocumentAwareness)
        {
            builder.Append(AddDocumentReferences(orchestrationContext, seenDocuments));
        }

        orchestrationContext.SystemMessageBuilder.Append(builder);
    }

    private async Task<List<(DocumentChunkSearchResult Result, string ReferenceType)>> SearchRelevantChunksAsync(
        PreemptiveRagContext context,
        InteractionDocumentOptions settings,
        List<(string ResourceId, string ReferenceType)> searchScopes,
        int topN)
    {
        if (searchScopes.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(settings.IndexProfileName))
        {
            return [];
        }

        var indexProfile = await _indexProfileStore.FindByNameAsync(settings.IndexProfileName);

        if (indexProfile == null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Document Preemptive RAG: index profile '{IndexProfileName}' not found.", settings.IndexProfileName);
            }

            return [];
        }

        var searchService = _serviceProvider.GetKeyedService<IVectorSearchService>(indexProfile.ProviderName);

        if (searchService == null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Document Preemptive RAG: no IVectorSearchService registered for provider '{ProviderName}'.", indexProfile.ProviderName);
            }

            return [];
        }

        var metadata = SearchIndexProfileEmbeddingMetadataAccessor.GetMetadata(indexProfile);
        var embeddingGenerator = await EmbeddingDeploymentResolver.CreateEmbeddingGeneratorAsync(
            _deploymentManager,
            _aiClientFactory,
            metadata,
            indexProfile.EmbeddingDeploymentId);

        if (embeddingGenerator == null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Document Preemptive RAG: embedding deployment is not configured or could not be resolved on index profile '{IndexProfileName}'. DeploymentId={DeploymentId}.",
                    settings.IndexProfileName,
                    metadata?.EmbeddingDeploymentId ?? indexProfile.EmbeddingDeploymentId ?? "(null)");
            }

            return [];
        }

        var embeddings = await embeddingGenerator.GenerateAsync(context.Queries);

        if (embeddings == null || embeddings.Count == 0)
        {
            return [];
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Document Preemptive RAG: searching {ScopeCount} scope(s): [{Scopes}] with {QueryCount} queries, topN={TopN}, retrievalMode={RetrievalMode}.",
                searchScopes.Count,
                string.Join(", ", searchScopes.Select(s => $"{s.ReferenceType}:{s.ResourceId}")),
                context.Queries.Count,
                topN,
                settings.RetrievalMode);
        }

        var allResults = new List<(DocumentChunkSearchResult Result, string ReferenceType)>();
        var seenChunkKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var embedding in embeddings)
        {
            if (embedding?.Vector == null)
            {
                continue;
            }

            foreach (var (scopeResourceId, scopeReferenceType) in searchScopes)
            {
                var results = await searchService.SearchAsync(
                    indexProfile,
                    embedding.Vector.ToArray(),
                    scopeResourceId,
                    scopeReferenceType,
                    topN);

                if (results == null)
                {
                    continue;
                }

                foreach (var result in results)
                {
                    if (result.Chunk == null || string.IsNullOrWhiteSpace(result.Chunk.Text))
                    {
                        continue;
                    }

                    var chunkKey = $"{result.DocumentKey}:{result.Chunk.Index}";

                    if (seenChunkKeys.Add(chunkKey))
                    {
                        allResults.Add((result, scopeReferenceType));
                    }
                }
            }
        }

        return allResults
            .OrderByDescending(r => r.Result.Score)
            .ToList();
    }

    private static InteractionDocumentOptions ResolveSettings(object resource, InteractionDocumentOptions defaults)
    {
        if (resource is CatalogItem item &&
            item.TryGet<DocumentsMetadata>(out var metadata))
        {
            return new InteractionDocumentOptions
            {
                IndexProfileName = defaults.IndexProfileName,
                TopN = metadata.DocumentTopN ?? defaults.TopN,
                RetrievalMode = metadata.RetrievalMode ?? defaults.RetrievalMode,
            };
        }

        return defaults;
    }

    private static List<(string ResourceId, string ReferenceType)> ResolveSearchScopes(PreemptiveRagContext context)
    {
        var searchScopes = new List<(string ResourceId, string ReferenceType)>();

        if (context.Resource is ChatInteraction interaction)
        {
            searchScopes.Add((interaction.ItemId, AIReferenceTypes.Document.ChatInteraction));
            return searchScopes;
        }

        if (context.Resource is not AIProfile profile)
        {
            return searchScopes;
        }

        searchScopes.Add((profile.ItemId, AIReferenceTypes.Document.Profile));

        if (context.OrchestrationContext.CompletionContext?.AdditionalProperties is not null &&
            context.OrchestrationContext.CompletionContext.AdditionalProperties.TryGetValue("Session", out var sessionObject) &&
            sessionObject is AIChatSession session &&
            session.Documents is { Count: > 0 })
        {
            searchScopes.Add((session.SessionId, AIReferenceTypes.Document.ChatSession));
        }

        return searchScopes;
    }

    private async Task<string> AppendHierarchicalContextAsync(
        IReadOnlyCollection<(DocumentChunkSearchResult Result, string ReferenceType)> results,
        int topN,
        bool showUserDocumentAwareness,
        bool keepProfileDocumentAwareness,
        AIInvocationContext invocationContext,
        Dictionary<string, (int Index, string FileName)> seenDocuments)
    {
        using var builder = ZString.CreateStringBuilder();
        var documentStore = _serviceProvider.GetService<IAIDocumentStore>();

        if (documentStore == null)
        {
            return AppendChunkContext(results.Take(topN), keepProfileDocumentAwareness, invocationContext, seenDocuments);
        }

        var selectedDocuments = results
            .Where(x => !string.IsNullOrWhiteSpace(x.Result.DocumentKey))
            .GroupBy(x => x.Result.DocumentKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                DocumentId = group.Key,
                Score = group.Max(x => x.Result.Score),
                FileName = group.Select(x => x.Result.FileName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                ReferenceType = group.Select(x => x.ReferenceType).First(),
            })
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .ToList();

        foreach (var documentEntry in selectedDocuments)
        {
            var document = await documentStore.FindByIdAsync(documentEntry.DocumentId);

            if (document == null)
            {
                continue;
            }

            if (!keepProfileDocumentAwareness && documentEntry.ReferenceType == AIReferenceTypes.Document.Profile)
            {
                builder.AppendLine("---");
                builder.AppendLine(await DocumentContextFormatter.FormatDocumentTextFromChunksAsync(_serviceProvider, document));
                continue;
            }

            if (!showUserDocumentAwareness)
            {
                builder.AppendLine("---");
                builder.AppendLine(await DocumentContextFormatter.FormatDocumentTextFromChunksAsync(_serviceProvider, document));
                continue;
            }

            var referenceIndex = invocationContext?.NextReferenceIndex() ?? seenDocuments.Count + 1;
            seenDocuments[document.ItemId] = (referenceIndex, _textNormalizer.NormalizeTitle(document.FileName));

            builder.AppendLine("---");
            builder.Append("[doc:");
            builder.Append(referenceIndex);
            builder.AppendLine("]");
            builder.AppendLine(await DocumentContextFormatter.FormatDocumentTextFromChunksAsync(_serviceProvider, document));
        }

        return builder.ToString();
    }

    private async Task<string> AppendFullUserDocumentContextAsync(
        IReadOnlyCollection<ChatDocumentInfo> documents,
        AIInvocationContext invocationContext,
        Dictionary<string, (int Index, string FileName)> seenDocuments)
    {
        if (documents.Count == 0)
        {
            return string.Empty;
        }

        var documentStore = _serviceProvider.GetService<IAIDocumentStore>();

        if (documentStore == null)
        {
            return string.Empty;
        }

        using var builder = ZString.CreateStringBuilder();

        foreach (var documentInfo in documents
            .Where(document => !string.IsNullOrWhiteSpace(document.DocumentId))
            .GroupBy(document => document.DocumentId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()))
        {
            var document = await documentStore.FindByIdAsync(documentInfo.DocumentId);

            if (document == null)
            {
                continue;
            }

            if (!seenDocuments.TryGetValue(document.ItemId, out var documentEntry))
            {
                documentEntry = (
                    invocationContext?.NextReferenceIndex() ?? seenDocuments.Count + 1,
                    _textNormalizer.NormalizeTitle(document.FileName ?? documentInfo.FileName));

                seenDocuments[document.ItemId] = documentEntry;
            }

            var referenceIndex = documentEntry.Index;

            builder.AppendLine("---");
            builder.Append("[doc:");
            builder.Append(referenceIndex);
            builder.AppendLine("]");
            builder.AppendLine(await DocumentContextFormatter.FormatDocumentTextFromChunksAsync(_serviceProvider, document));
        }

        return builder.ToString();
    }

    private string AppendChunkContext(
        IEnumerable<(DocumentChunkSearchResult Result, string ReferenceType)> results,
        bool keepProfileDocumentAwareness,
        AIInvocationContext invocationContext,
        Dictionary<string, (int Index, string FileName)> seenDocuments)
    {
        using var builder = ZString.CreateStringBuilder();

        foreach (var (result, scopeReferenceType) in results)
        {
            if (!keepProfileDocumentAwareness && scopeReferenceType == AIReferenceTypes.Document.Profile)
            {
                builder.AppendLine("---");
                builder.AppendLine(result.Chunk.Text);
                continue;
            }

            var documentKey = result.DocumentKey;

            if (!string.IsNullOrEmpty(documentKey) && !seenDocuments.ContainsKey(documentKey))
            {
                seenDocuments[documentKey] = (invocationContext?.NextReferenceIndex() ?? seenDocuments.Count + 1, _textNormalizer.NormalizeTitle(result.FileName));
            }

            var referenceIndex = !string.IsNullOrEmpty(documentKey) && seenDocuments.TryGetValue(documentKey, out var entry)
                ? entry.Index
                : invocationContext?.NextReferenceIndex() ?? seenDocuments.Count + 1;

            builder.AppendLine("---");
            builder.Append("[doc:");
            builder.Append(referenceIndex);
            builder.Append("] ");
            builder.AppendLine(result.Chunk.Text);
        }

        return builder.ToString();
    }

    private static string AddDocumentReferences(
        OrchestrationContext orchestrationContext,
        Dictionary<string, (int Index, string FileName)> seenDocuments)
    {
        using var builder = ZString.CreateStringBuilder();

        if (seenDocuments.Count == 0)
        {
            return string.Empty;
        }

        builder.AppendLine();
        builder.AppendLine("References:");

        foreach (var kvp in seenDocuments)
        {
            builder.Append("[doc:");
            builder.Append(kvp.Value.Index);
            builder.Append("] = {DocumentId: \"");
            builder.Append(kvp.Key);
            builder.Append('"');

            if (!string.IsNullOrWhiteSpace(kvp.Value.FileName))
            {
                builder.Append(", FileName: \"");
                builder.Append(kvp.Value.FileName);
                builder.Append('"');
            }

            builder.AppendLine("}");
        }

        var citationMap = new Dictionary<string, AICompletionReference>();

        foreach (var kvp in seenDocuments)
        {
            var template = $"[doc:{kvp.Value.Index}]";
            citationMap[template] = new AICompletionReference
            {
                Text = string.IsNullOrWhiteSpace(kvp.Value.FileName) ? template : kvp.Value.FileName,
                Title = kvp.Value.FileName,
                Index = kvp.Value.Index,
                ReferenceId = kvp.Key,
                ReferenceType = AIReferenceTypes.DataSource.Document,
            };
        }

        orchestrationContext.Properties["DocumentReferences"] = citationMap;
        return builder.ToString();
    }
}
