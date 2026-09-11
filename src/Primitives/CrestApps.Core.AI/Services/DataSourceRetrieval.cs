using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Support;
using Cysharp.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Runs a vector search against one AI data source knowledge base and renders the matches as prompt-ready
/// text with citations. The query is embedded with the same embedding deployment the knowledge base index
/// profile was indexed with, so a phrase searched here lands in the same vector space as the stored chunks.
/// </summary>
/// <remarks>
/// This is shared by the profile-bound <c>DataSourceSearchTool</c> and by the configured data source search
/// tool instances, so both honor identical retrieval parameters and produce identically formatted results.
/// </remarks>
internal static class DataSourceRetrieval
{
    /// <summary>
    /// The most phrases one search may embed and run. Each phrase costs its own index query, and a model
    /// handed an array will happily send six rewordings of one idea — which retrieves nearly the same chunks
    /// six times over. Extra phrases are dropped rather than rejected, so an over-eager caller still gets an
    /// answer instead of an error it has to recover from.
    /// </summary>
    public const int MaxQueries = 3;

    /// <summary>
    /// Searches the requested data source and returns the text to hand back to the AI model.
    /// </summary>
    /// <param name="services">The request services used to resolve the stores, index provider, and embedding generator.</param>
    /// <param name="request">The query-time retrieval parameters.</param>
    /// <param name="toolName">The name of the calling tool, used for logging.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The formatted search results, or a message explaining why no content could be returned.</returns>
    public static async Task<string> SearchAsync(
        IServiceProvider services,
        DataSourceRetrievalRequest request,
        string toolName,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(request);

        var queries = NormalizeQueries(request.Queries);

        if (queries.Count == 0)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: no search phrase was supplied.", toolName);

            return "No search phrase was supplied. Provide at least one phrase to search for.";
        }

        var dataSourceStore = services.GetRequiredService<IAIDataSourceStore>();
        var dataSource = await dataSourceStore.FindByIdAsync(request.DataSourceId, cancellationToken);

        if (dataSource == null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: data source '{DataSourceId}' was not found.", toolName, request.DataSourceId);

            return $"Data source '{request.DataSourceId}' was not found.";
        }

        if (string.IsNullOrEmpty(dataSource.AIKnowledgeBaseIndexProfileName))
        {
            logger.LogWarning("AI tool '{ToolName}' failed: no knowledge base index configured for data source '{DataSourceId}'.", toolName, request.DataSourceId);

            return "No knowledge base index is configured for this data source. Please configure a knowledge base index in the data source settings.";
        }

        var indexProfileStore = services.GetRequiredService<ISearchIndexProfileStore>();
        var masterIndexProfile = await indexProfileStore.FindByNameAsync(dataSource.AIKnowledgeBaseIndexProfileName, cancellationToken);

        if (masterIndexProfile == null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: knowledge base index '{IndexProfileName}' was not found.", toolName, dataSource.AIKnowledgeBaseIndexProfileName);

            return $"Knowledge base index '{dataSource.AIKnowledgeBaseIndexProfileName}' was not found.";
        }

        var contentManager = services.GetKeyedService<IDataSourceContentManager>(masterIndexProfile.ProviderName);

        if (contentManager == null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: no vector search service for provider '{ProviderName}'.", toolName, masterIndexProfile.ProviderName);

            return $"No vector search service is available for provider '{masterIndexProfile.ProviderName}'.";
        }

        var embeddingGenerator = await CreateEmbeddingGeneratorAsync(services, masterIndexProfile, cancellationToken);

        if (embeddingGenerator == null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: embedding configuration is missing for the knowledge base index.", toolName);

            return "Embedding configuration is missing for the knowledge base index.";
        }

        // One batched call regardless of how many phrases were asked for: the embedding API takes the whole
        // set, so N phrases cost one round trip rather than N.
        var embeddings = await embeddingGenerator.GenerateAsync(queries, cancellationToken: cancellationToken);

        var vectors = embeddings?
            .Where(embedding => embedding?.Vector != null)
            .Select(embedding => embedding.Vector.ToArray())
            .ToList() ?? [];

        if (vectors.Count == 0)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: could not generate embeddings for the search phrases.", toolName);

            return "Failed to generate embeddings for the search phrases.";
        }

        var siteSettings = services.GetRequiredService<IOptionsMonitor<AIDataSourceOptions>>().CurrentValue;
        var topN = siteSettings.GetTopNDocuments(request.TopNDocuments);

        string providerFilter = null;

        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            var filterTranslator = services.GetKeyedService<IODataFilterTranslator>(masterIndexProfile.ProviderName);

            if (filterTranslator != null)
            {
                providerFilter = filterTranslator.Translate(request.Filter);
            }
            else
            {
                logger.LogWarning("No OData filter translator available for provider '{ProviderName}'. Filter will be ignored.", masterIndexProfile.ProviderName);
            }
        }

        var candidateCount = DataSourceSearchResultSelector.GetCandidateCount(topN);

        // The phrases are independent queries against a thread-safe index client, so they run together rather
        // than one after another. That matters on a realtime session, where a grounded turn cannot start
        // speaking until retrieval returns.
        var resultSets = await Task.WhenAll(vectors.Select(vector => contentManager.SearchAsync(
            masterIndexProfile,
            vector,
            request.DataSourceId,
            candidateCount,
            providerFilter,
            cancellationToken)));

        if (resultSets.All(resultSet => resultSet == null || !resultSet.Any()))
        {
            return BuildEmptyResultMessage(
                "No relevant content was found in the data source for this query.",
                request.IsInScope);
        }

        var minimumScore = siteSettings.GetMinimumScore(request.Strictness);
        var selected = DataSourceSearchResultSelector.FuseTopResults(resultSets, topN, minimumScore);

        if (selected.Count == 0)
        {
            return BuildEmptyResultMessage(
                "No results met the strictness and quality thresholds.",
                request.IsInScope);
        }

        var textNormalizer = services.GetRequiredService<IAITextNormalizer>();
        var references = new ReferenceCollector(AIInvocationScope.Current);

        using var builder = ZString.CreateStringBuilder();
        builder.AppendLine("Relevant content from data source:");

        if (request.RetrievalMode == DataSourceRetrievalMode.Hierarchical)
        {
            builder.Append(await AppendHierarchicalContextAsync(
                services,
                dataSource,
                selected,
                topN,
                textNormalizer,
                references,
                logger,
                cancellationToken));
        }
        else
        {
            builder.Append(AppendChunkContext(selected, textNormalizer, references));
        }

        builder.Append(references.Render());

        return builder.ToString();
    }

    /// <summary>
    /// Builds the message returned when a search finds nothing worth returning.
    /// </summary>
    /// <param name="reason">The plain statement of what happened.</param>
    /// <param name="isInScope">The caller's answering policy, when it has one.</param>
    /// <returns>The message to hand back to the AI model.</returns>
    private static string BuildEmptyResultMessage(string reason, bool? isInScope)
    {
        return isInScope switch
        {
            true => $"{reason} The answer is not available in the configured data source.",
            false => $"{reason} Answer using your general knowledge instead.",
            _ => reason,
        };
    }

    /// <summary>
    /// Trims the requested phrases to the set actually worth searching: blanks dropped, exact repeats
    /// collapsed, and the remainder capped at <see cref="MaxQueries"/>.
    /// </summary>
    /// <param name="queries">The requested phrases.</param>
    /// <returns>The phrases to embed and search.</returns>
    private static List<string> NormalizeQueries(IReadOnlyList<string> queries)
    {
        if (queries is not { Count: > 0 })
        {
            return [];
        }

        var normalized = new List<string>(Math.Min(queries.Count, MaxQueries));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var query in queries)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                continue;
            }

            var trimmed = query.Trim();

            // An exact repeat costs a whole index query and returns the same chunks, so it never survives.
            // Near-synonyms cannot be caught here — the schema description is what discourages those.
            if (!seen.Add(trimmed))
            {
                continue;
            }

            normalized.Add(trimmed);

            if (normalized.Count == MaxQueries)
            {
                break;
            }
        }

        return normalized;
    }

    /// <summary>
    /// Creates the embedding generator the supplied knowledge base index profile was indexed with, so a
    /// query is embedded by the same model that produced the stored chunk vectors.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="indexProfile">The knowledge base index profile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The embedding generator, or <see langword="null"/> when no embedding deployment is configured.</returns>
    private static async Task<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(
        IServiceProvider services,
        SearchIndexProfile indexProfile,
        CancellationToken cancellationToken)
    {
        // Resolve the query-embedding deployment exactly the way the indexing service does: it lives on
        // the index profile itself (indexProfile.EmbeddingDeploymentName) and is only optionally overridden
        // by the data-source metadata. The metadata being absent must NOT fail search, or a profile that
        // indexed fine using the top-level embedding deployment could never be queried.
        var deploymentName = indexProfile.EmbeddingDeploymentName;

        if (indexProfile.TryGet(out DataSourceIndexProfileMetadata profileMetadata) &&
            !string.IsNullOrEmpty(profileMetadata.EmbeddingDeploymentName))
        {
            deploymentName = profileMetadata.EmbeddingDeploymentName;
        }

        if (string.IsNullOrWhiteSpace(deploymentName))
        {
            return null;
        }

        var deployment = await services.GetRequiredService<IAIDeploymentManager>().FindByNameAsync(deploymentName, cancellationToken);

        if (deployment == null)
        {
            return null;
        }

        return await services.GetRequiredService<IAIClientFactory>().CreateEmbeddingGeneratorAsync(deployment);
    }

    /// <summary>
    /// Renders each matching chunk on its own, which keeps the injected context as small as the match itself.
    /// </summary>
    /// <param name="results">The selected search results.</param>
    /// <param name="textNormalizer">The text normalizer used to clean citation titles.</param>
    /// <param name="references">The citation collector.</param>
    /// <returns>The rendered chunks.</returns>
    private static string AppendChunkContext(
        IReadOnlyList<DataSourceSearchResult> results,
        IAITextNormalizer textNormalizer,
        ReferenceCollector references)
    {
        using var builder = ZString.CreateStringBuilder();

        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.Content))
            {
                continue;
            }

            var entry = references.Track(result.ReferenceId, ResolveReferenceTitle(textNormalizer, result.Title, result.ReferenceId), result.ReferenceType);

            builder.AppendLine("---");

            if (!string.IsNullOrWhiteSpace(entry.Title))
            {
                builder.Append(entry.Label);
                builder.Append(" Title: ");
                builder.AppendLine(entry.Title);
            }

            builder.Append(entry.Label);
            builder.Append(' ');
            builder.AppendLine(result.Content);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders the full source document behind each match instead of the matching chunk alone. Matches are
    /// collapsed per source document, and every document is read back through the data source's own source
    /// handler so the model sees the complete text rather than a window into it.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="dataSource">The data source being searched.</param>
    /// <param name="results">The selected search results.</param>
    /// <param name="topN">The maximum number of source documents to return.</param>
    /// <param name="textNormalizer">The text normalizer used to clean citation titles.</param>
    /// <param name="references">The citation collector.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rendered documents, falling back to the chunk rendering when the documents cannot be read.</returns>
    private static async Task<string> AppendHierarchicalContextAsync(
        IServiceProvider services,
        AIDataSource dataSource,
        IReadOnlyList<DataSourceSearchResult> results,
        int topN,
        IAITextNormalizer textNormalizer,
        ReferenceCollector references,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var sourceType = AIDataSourceSourceHelper.GetSource(dataSource);
        var sourceHandler = services.GetKeyedService<IAIDataSourceSourceHandler>(sourceType);

        var groups = results
            .Where(result => !string.IsNullOrWhiteSpace(result.ReferenceId))
            .GroupBy(result => result.ReferenceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                ReferenceId = group.Key,
                Score = group.Max(result => result.Score),
                Title = group.Select(result => result.Title).FirstOrDefault(title => !string.IsNullOrWhiteSpace(title)),
                ReferenceType = group.Select(result => result.ReferenceType).FirstOrDefault(type => !string.IsNullOrWhiteSpace(type)),
                Chunks = group.OrderBy(result => result.ChunkIndex).Select(result => result.Content).ToList(),
            })
            .OrderByDescending(group => group.Score)
            .Take(topN)
            .ToList();

        if (sourceHandler == null || groups.Count == 0)
        {
            if (sourceHandler == null)
            {
                logger.LogWarning("Hierarchical retrieval is unavailable because source type '{SourceType}' is not registered. Falling back to chunk retrieval.", sourceType);
            }

            return AppendChunkContext(results, textNormalizer, references);
        }

        var documents = new Dictionary<string, SourceDocument>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await foreach (var pair in sourceHandler.ReadByIdsAsync(dataSource, groups.Select(group => group.ReferenceId), cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                {
                    documents[pair.Key] = pair.Value;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A source that can no longer be read (a deleted index, revoked credentials) must not sink the
            // search: the matching chunks are already in hand, so degrade to chunk retrieval instead.
            logger.LogWarning(ex, "Failed to read full documents for data source '{DataSourceId}'. Falling back to chunk retrieval.", dataSource.ItemId);

            return AppendChunkContext(results, textNormalizer, references);
        }

        using var builder = ZString.CreateStringBuilder();

        foreach (var group in groups)
        {
            documents.TryGetValue(group.ReferenceId, out var document);

            var content = string.IsNullOrWhiteSpace(document?.Content)
                ? string.Join("\n", group.Chunks.Where(chunk => !string.IsNullOrWhiteSpace(chunk)))
                : document.Content;

            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var title = ResolveReferenceTitle(textNormalizer, document?.Title ?? group.Title, group.ReferenceId);
            var entry = references.Track(group.ReferenceId, title, group.ReferenceType);

            builder.AppendLine("---");

            if (!string.IsNullOrWhiteSpace(entry.Title))
            {
                builder.Append(entry.Label);
                builder.Append(" Title: ");
                builder.AppendLine(entry.Title);
            }

            builder.Append(entry.Label);
            builder.Append(' ');
            builder.AppendLine(content);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Resolves a citation title that never exposes a serialized source document.
    /// </summary>
    /// <param name="textNormalizer">The text normalizer.</param>
    /// <param name="title">The indexed document title.</param>
    /// <param name="referenceId">The document reference identifier used as the fallback title.</param>
    /// <returns>The resolved citation title.</returns>
    private static string ResolveReferenceTitle(IAITextNormalizer textNormalizer, string title, string referenceId)
    {
        var normalizedTitle = textNormalizer.NormalizeTitle(title);

        if (string.IsNullOrWhiteSpace(normalizedTitle) || DocumentTitleResolver.LooksLikeSerializedDocument(normalizedTitle))
        {
            return referenceId;
        }

        return normalizedTitle;
    }

    /// <summary>
    /// Assigns one citation index per source document and renders the trailing reference list, registering
    /// each citation on the active invocation context so the UI can turn it into a link.
    /// </summary>
    private sealed class ReferenceCollector
    {
        private readonly Dictionary<string, (int Index, string Title, string ReferenceType)> _seen = new(StringComparer.OrdinalIgnoreCase);
        private readonly AIInvocationContext _invocationContext;
        private int _fallbackIndex;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReferenceCollector"/> class.
        /// </summary>
        /// <param name="invocationContext">The active invocation context, or <see langword="null"/> when the tool runs outside one.</param>
        public ReferenceCollector(AIInvocationContext invocationContext)
        {
            _invocationContext = invocationContext;
        }

        /// <summary>
        /// Records one rendered result and returns the citation label and title to print with it.
        /// </summary>
        /// <param name="referenceId">The source document reference identifier.</param>
        /// <param name="title">The resolved citation title.</param>
        /// <param name="referenceType">The reference type used to build links.</param>
        public (string Label, string Title) Track(string referenceId, string title, string referenceType)
        {
            if (string.IsNullOrEmpty(referenceId))
            {
                return ($"[doc:{NextIndex()}]", title);
            }

            if (!_seen.TryGetValue(referenceId, out var entry))
            {
                entry = (NextIndex(), title, referenceType);
                _seen[referenceId] = entry;
            }

            return ($"[doc:{entry.Index}]", entry.Title);
        }

        /// <summary>
        /// Renders the reference list appended after the retrieved content.
        /// </summary>
        public string Render()
        {
            if (_seen.Count == 0)
            {
                return string.Empty;
            }

            using var builder = ZString.CreateStringBuilder();
            builder.AppendLine();
            builder.AppendLine("References:");

            foreach (var kvp in _seen)
            {
                builder.Append("[doc:");
                builder.Append(kvp.Value.Index);
                builder.Append("] = ");
                builder.AppendLine(kvp.Key);
            }

            if (_invocationContext != null)
            {
                foreach (var kvp in _seen)
                {
                    var template = $"[doc:{kvp.Value.Index}]";

                    _invocationContext.ToolReferences.TryAdd(template, new AICompletionReference
                    {
                        Text = string.IsNullOrWhiteSpace(kvp.Value.Title) ? template : kvp.Value.Title,
                        Title = kvp.Value.Title,
                        Index = kvp.Value.Index,
                        ReferenceId = kvp.Key,
                        ReferenceType = kvp.Value.ReferenceType,
                    });
                }
            }

            return builder.ToString();
        }

        private int NextIndex()
        {
            return _invocationContext?.NextReferenceIndex() ?? ++_fallbackIndex;
        }
    }
}
