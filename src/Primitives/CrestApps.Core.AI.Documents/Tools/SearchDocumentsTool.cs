using System.Text.Json;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Services;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using Cysharp.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tools;

/// <summary>
/// Searches indexed document knowledge across the active AI profile or chat interaction.
/// </summary>
public sealed class SearchDocumentsTool : AIFunction
{
    public const string TheName = SystemToolNames.SearchDocuments;

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "query": {
          "type": "string",
          "description": "The search query to find relevant content in available document knowledge."
        },
        "top_n": {
          "type": "integer",
          "description": "Number of top matches to return. Defaults to 3."
        }
      },
      "required": ["query"],
      "additionalProperties": false
    }
    """);

    public override string Name => TheName;

    public override string Description => "Searches available document knowledge using semantic vector search and returns the most relevant matching context. If no relevant content is found, report that the documents do not contain the answer.";

    public override JsonElement JsonSchema => _jsonSchema;

    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>
        {
            ["Strict"] = false,
        };

    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetRequiredService<ILogger<SearchDocumentsTool>>();

        if (!arguments.TryGetFirstString("query", out var query))
        {
            logger.LogWarning("AI tool '{ToolName}' missing required argument 'query'.", Name);

            return "Unable to find a 'query' argument in the arguments parameter.";
        }

        try
        {
            var invocationContext = AIInvocationScope.Current;
            var executionContext = invocationContext?.ToolExecutionContext;
            var textNormalizer = arguments.Services.GetRequiredService<IAITextNormalizer>();
            var searchScopes = ResolveSearchScopes(invocationContext, executionContext?.Resource);

            if (searchScopes.Count == 0)
            {
                logger.LogWarning("AI tool '{ToolName}' failed: no active chat interaction session or AI profile.", Name);

                return "Document search requires an active chat interaction session or AI profile.";
            }

            var showUserDocumentAwareness =
                executionContext?.Resource is not AIProfile ||
                searchScopes.Any(scope => scope.ReferenceType == AIReferenceTypes.Document.ChatSession);

            var isStrictScope = executionContext?.Resource switch
            {
                AIProfile profile => profile.TryGet<AIDataSourceRagMetadata>(out var profileMetadata) && profileMetadata.IsInScope,
                ChatInteraction chatInteraction => chatInteraction.TryGet<AIDataSourceRagMetadata>(out var interactionMetadata) && interactionMetadata.IsInScope,
                _ => false,
            };

            var snapshotSettings = arguments.Services.GetService<IOptionsSnapshot<InteractionDocumentOptions>>()?.Value;
            var optionsSettings = arguments.Services.GetRequiredService<IOptions<InteractionDocumentOptions>>().Value;
            var defaultSettings = !string.IsNullOrWhiteSpace(snapshotSettings?.IndexProfileName)
                ? snapshotSettings
                : optionsSettings;
            var settings = ResolveSettings(executionContext?.Resource, defaultSettings);

            if (string.IsNullOrWhiteSpace(settings.IndexProfileName))
            {
                logger.LogWarning("AI tool '{ToolName}' failed: no index profile is configured.", Name);

                return "Document search is not configured. No index profile is set.";
            }

            var indexProfileStore = arguments.Services.GetRequiredService<ISearchIndexProfileStore>();
            var indexProfile = await indexProfileStore.FindByNameAsync(settings.IndexProfileName, cancellationToken);

            if (indexProfile == null)
            {
                logger.LogWarning("AI tool '{ToolName}' failed: index profile '{IndexProfileName}' was not found.", Name, settings.IndexProfileName);

                return $"Index profile '{settings.IndexProfileName}' was not found.";
            }

            var searchService = arguments.Services.GetKeyedService<IVectorSearchService>(indexProfile.ProviderName);

            if (searchService == null)
            {
                logger.LogWarning("AI tool '{ToolName}' failed: no search service available for provider '{ProviderName}'.", Name, indexProfile.ProviderName);

                return $"No search service is available for provider '{indexProfile.ProviderName}'.";
            }

            var aiClientFactory = arguments.Services.GetRequiredService<IAIClientFactory>();
            var deploymentManager = arguments.Services.GetRequiredService<IAIDeploymentManager>();
            var embeddingDeployment = await deploymentManager.ResolveOrDefaultAsync(AIDeploymentType.Embedding, clientName: executionContext?.ClientName, cancellationToken: cancellationToken);

            if (embeddingDeployment == null)
            {
                logger.LogWarning("AI tool '{ToolName}' failed: no embedding deployment configured.", Name);

                return "No embedding deployment is configured for document search.";
            }

            var embeddingGenerator = await aiClientFactory.CreateEmbeddingGeneratorAsync(embeddingDeployment);

            if (embeddingGenerator == null)
            {
                logger.LogWarning("AI tool '{ToolName}' failed: could not create embedding generator.", Name);

                return "Failed to create embedding generator for document search.";
            }

            var embeddings = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);

            if (embeddings is null || embeddings.Count == 0 || embeddings[0]?.Vector is null)
            {
                logger.LogWarning("AI tool '{ToolName}' failed: could not generate embedding for query.", Name);

                return "Failed to generate embedding for the search query.";
            }

            var topN = arguments.GetFirstValueOrDefault("top_n", settings.TopN);

            if (topN <= 0)
            {
                topN = 3;
            }

            var results = await SearchAsync(
                searchService,
                indexProfile,
                embeddings[0].Vector.ToArray(),
                searchScopes,
                topN,
                cancellationToken);

            if (results.Count == 0)
            {
                if (showUserDocumentAwareness)
                {
                    return isStrictScope
                        ? "No relevant content was found in the uploaded documents for this query. Tell the user the uploaded documents do not contain the answer."
                        : "No relevant content was found in the uploaded documents for this query.";
                }

                return isStrictScope
                    ? "No relevant background knowledge content was found for this query. Tell the user the available knowledge does not contain the answer."
                    : "No relevant background knowledge content was found for this query.";
            }

            var hasProfileScope = searchScopes.Any(scope => scope.ReferenceType == AIReferenceTypes.Document.Profile);
            var hasSessionScope = searchScopes.Any(scope => scope.ReferenceType == AIReferenceTypes.Document.ChatSession);
            var keepProfileDocumentAwareness = !(executionContext?.Resource is AIProfile && hasProfileScope && hasSessionScope);

            using var builder = ZString.CreateStringBuilder();
            builder.AppendLine(showUserDocumentAwareness
                ? "Relevant content from uploaded documents:"
                : "Relevant background knowledge content:");

            if (settings.RetrievalMode == DocumentRetrievalMode.Hierarchical)
            {
                builder.Append(await AppendHierarchicalContextAsync(
                    arguments.Services,
                    results,
                    topN,
                    showUserDocumentAwareness,
                    keepProfileDocumentAwareness,
                    invocationContext,
                    textNormalizer));
            }
            else
            {
                builder.Append(AppendChunkContext(
                    results.Take(topN),
                    showUserDocumentAwareness,
                    keepProfileDocumentAwareness,
                    invocationContext,
                    textNormalizer));
            }

            return builder.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during document search.");

            return "An error occurred while searching documents.";
        }
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

    private static List<(string ResourceId, string ReferenceType)> ResolveSearchScopes(AIInvocationContext invocationContext, object resource)
    {
        var searchScopes = new List<(string ResourceId, string ReferenceType)>();

        if (resource is ChatInteraction interaction)
        {
            searchScopes.Add((interaction.ItemId, AIReferenceTypes.Document.ChatInteraction));
            return searchScopes;
        }

        if (resource is not AIProfile profile)
        {
            return searchScopes;
        }

        searchScopes.Add((profile.ItemId, AIReferenceTypes.Document.Profile));

        if (invocationContext?.Items.TryGetValue(nameof(AIChatSession), out var sessionObj) == true &&
            sessionObj is AIChatSession session &&
            session.Documents is { Count: > 0 })
        {
            searchScopes.Add((session.SessionId, AIReferenceTypes.Document.ChatSession));
        }

        return searchScopes;
    }

    private static async Task<List<(DocumentChunkSearchResult Result, string ReferenceType)>> SearchAsync(
        IVectorSearchService searchService,
        SearchIndexProfile indexProfile,
        float[] embedding,
        IReadOnlyCollection<(string ResourceId, string ReferenceType)> searchScopes,
        int topN,
        CancellationToken cancellationToken)
    {
        var allResults = new List<(DocumentChunkSearchResult Result, string ReferenceType)>();
        var seenChunkKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (scopeResourceId, scopeReferenceType) in searchScopes)
        {
            var results = await searchService.SearchAsync(
                indexProfile,
                embedding,
                scopeResourceId,
                scopeReferenceType,
                topN,
                cancellationToken);

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

        return allResults
            .OrderByDescending(entry => entry.Result.Score)
            .ToList();
    }

    private static async Task<string> AppendHierarchicalContextAsync(
        IServiceProvider services,
        IReadOnlyCollection<(DocumentChunkSearchResult Result, string ReferenceType)> results,
        int topN,
        bool showUserDocumentAwareness,
        bool keepProfileDocumentAwareness,
        AIInvocationContext invocationContext,
        IAITextNormalizer textNormalizer)
    {
        using var builder = ZString.CreateStringBuilder();
        var documentStore = services.GetService<IAIDocumentStore>();

        if (documentStore == null)
        {
            return AppendChunkContext(results.Take(topN), showUserDocumentAwareness, keepProfileDocumentAwareness, invocationContext, textNormalizer);
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

        if (selectedDocuments.Count == 0)
        {
            return AppendChunkContext(results.Take(topN), showUserDocumentAwareness, keepProfileDocumentAwareness, invocationContext, textNormalizer);
        }

        var seenDocuments = new Dictionary<string, (int Index, string FileName)>(StringComparer.OrdinalIgnoreCase);

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
                builder.AppendLine(await DocumentContextFormatter.FormatDocumentTextFromChunksAsync(services, document));
                continue;
            }

            if (!showUserDocumentAwareness)
            {
                builder.AppendLine("---");
                builder.AppendLine(await DocumentContextFormatter.FormatDocumentTextFromChunksAsync(services, document));
                continue;
            }

            var referenceIndex = invocationContext?.NextReferenceIndex() ?? seenDocuments.Count + 1;
            seenDocuments[document.ItemId] = (referenceIndex, textNormalizer.NormalizeTitle(document.FileName));

            builder.AppendLine("---");
            builder.Append("[doc:");
            builder.Append(referenceIndex);
            builder.AppendLine("]");
            builder.AppendLine(await DocumentContextFormatter.FormatDocumentTextFromChunksAsync(services, document));
        }

        builder.Append(AddDocumentReferences(invocationContext, seenDocuments));
        return builder.ToString();
    }

    private static string AppendChunkContext(
        IEnumerable<(DocumentChunkSearchResult Result, string ReferenceType)> results,
        bool showUserDocumentAwareness,
        bool keepProfileDocumentAwareness,
        AIInvocationContext invocationContext,
        IAITextNormalizer textNormalizer)
    {
        using var builder = ZString.CreateStringBuilder();

        if (!showUserDocumentAwareness)
        {
            foreach (var (result, _) in results)
            {
                if (result.Chunk == null || string.IsNullOrWhiteSpace(result.Chunk.Text))
                {
                    continue;
                }

                builder.AppendLine("---");
                builder.AppendLine(result.Chunk.Text);
            }

            return builder.ToString();
        }

        var seenDocuments = new Dictionary<string, (int Index, string FileName)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (result, scopeReferenceType) in results)
        {
            if (result.Chunk == null || string.IsNullOrWhiteSpace(result.Chunk.Text))
            {
                continue;
            }

            if (!keepProfileDocumentAwareness && scopeReferenceType == AIReferenceTypes.Document.Profile)
            {
                builder.AppendLine("---");
                builder.AppendLine(result.Chunk.Text);
                continue;
            }

            var documentKey = result.DocumentKey;

            if (!string.IsNullOrEmpty(documentKey) && !seenDocuments.ContainsKey(documentKey))
            {
                seenDocuments[documentKey] = (invocationContext?.NextReferenceIndex() ?? seenDocuments.Count + 1, textNormalizer.NormalizeTitle(result.FileName));
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

        builder.Append(AddDocumentReferences(invocationContext, seenDocuments));
        return builder.ToString();
    }

    private static string AddDocumentReferences(
        AIInvocationContext invocationContext,
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

        if (invocationContext == null)
        {
            return builder.ToString();
        }

        foreach (var kvp in seenDocuments)
        {
            var template = $"[doc:{kvp.Value.Index}]";
            invocationContext.ToolReferences.TryAdd(template, new AICompletionReference
            {
                Text = string.IsNullOrWhiteSpace(kvp.Value.FileName) ? template : kvp.Value.FileName,
                Title = kvp.Value.FileName,
                Index = kvp.Value.Index,
                ReferenceId = kvp.Key,
                ReferenceType = AIReferenceTypes.DataSource.Document,
            });
        }

        return builder.ToString();
    }
}
