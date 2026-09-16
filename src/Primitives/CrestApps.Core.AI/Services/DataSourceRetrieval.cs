using System.Text.RegularExpressions;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Infrastructure;
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
internal static partial class DataSourceRetrieval
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
        var result = await SearchDetailedAsync(services, request, toolName, logger, cancellationToken);

        return result.Text;
    }

    /// <summary>
    /// Searches the requested data source and returns the text to hand back to the AI model along with the
    /// figures and tables among the hits.
    /// </summary>
    /// <param name="services">The request services used to resolve the stores, index provider, and embedding generator.</param>
    /// <param name="request">The query-time retrieval parameters.</param>
    /// <param name="toolName">The name of the calling tool, used for logging.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The search results.</returns>
    public static async Task<DataSourceRetrievalResult> SearchDetailedAsync(
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

            return Message("No search phrase was supplied. Provide at least one phrase to search for.");
        }

        var dataSourceStore = services.GetRequiredService<IAIDataSourceStore>();
        var dataSource = await dataSourceStore.FindByIdAsync(request.DataSourceId, cancellationToken);

        if (dataSource == null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: data source '{DataSourceId}' was not found.", toolName, request.DataSourceId);

            return Message($"Data source '{request.DataSourceId}' was not found.");
        }

        if (string.IsNullOrEmpty(dataSource.AIKnowledgeBaseIndexProfileName))
        {
            logger.LogWarning("AI tool '{ToolName}' failed: no knowledge base index configured for data source '{DataSourceId}'.", toolName, request.DataSourceId);

            return Message("No knowledge base index is configured for this data source. Please configure a knowledge base index in the data source settings.");
        }

        var indexProfileStore = services.GetRequiredService<ISearchIndexProfileStore>();
        var masterIndexProfile = await indexProfileStore.FindByNameAsync(dataSource.AIKnowledgeBaseIndexProfileName, cancellationToken);

        if (masterIndexProfile == null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: knowledge base index '{IndexProfileName}' was not found.", toolName, dataSource.AIKnowledgeBaseIndexProfileName);

            return Message($"Knowledge base index '{dataSource.AIKnowledgeBaseIndexProfileName}' was not found.");
        }

        var contentManager = services.GetKeyedService<IDataSourceContentManager>(masterIndexProfile.ProviderName);

        if (contentManager == null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: no vector search service for provider '{ProviderName}'.", toolName, masterIndexProfile.ProviderName);

            return Message($"No vector search service is available for provider '{masterIndexProfile.ProviderName}'.");
        }

        var embeddingGenerator = await CreateEmbeddingGeneratorAsync(services, masterIndexProfile, cancellationToken);

        if (embeddingGenerator == null)
        {
            logger.LogWarning("AI tool '{ToolName}' failed: embedding configuration is missing for the knowledge base index.", toolName);

            return Message("Embedding configuration is missing for the knowledge base index.");
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

            return Message("Failed to generate embeddings for the search phrases.");
        }

        var siteSettings = services.GetRequiredService<IOptionsMonitor<AIDataSourceOptions>>().CurrentValue;
        var topN = siteSettings.GetTopNDocuments(request.TopNDocuments);

        // Only a data source that stores typed knowledge objects has rows carrying the typed columns.
        // Anywhere else the same names mean the caller's own fields, so they are addressed through the
        // per-row filter bag rather than claimed by the knowledge base.
        var storesTypedKnowledge = string.Equals(
            AIDataSourceSourceHelper.GetSource(dataSource),
            AIDataSourceSourceTypes.File,
            StringComparison.OrdinalIgnoreCase);

        var callerFilter = storesTypedKnowledge ? request.Filter : QualifyCallerFields(request.Filter);
        var contentTypeClause = BuildContentTypeClause(request.ContentTypes);
        var filter = CombineFilters(callerFilter, contentTypeClause);

        IODataFilterTranslator filterTranslator = null;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            filterTranslator = services.GetKeyedService<IODataFilterTranslator>(masterIndexProfile.ProviderName);

            if (filterTranslator == null)
            {
                logger.LogWarning("No OData filter translator available for provider '{ProviderName}'. Filter will be ignored.", masterIndexProfile.ProviderName);
            }
        }

        var providerFilter = Translate(filterTranslator, filter);
        var candidateCount = DataSourceSearchResultSelector.GetCandidateCount(topN);

        var resultSets = await SearchIndexAsync(
            contentManager,
            masterIndexProfile,
            vectors,
            request.DataSourceId,
            candidateCount,
            providerFilter,
            cancellationToken);

        var contentTypeFilterDropped = false;

        if (contentTypeClause != null && providerFilter != null && IsEmpty(resultSets))
        {
            // An index built before the typed columns existed cannot honor a filter that names one, and
            // every provider reports that by logging and handing back nothing rather than by throwing — so
            // an empty result is the only signal there is to read. Searching again without the narrowing
            // costs one round trip on a search that had nothing to return anyway, and turns a search that
            // failed closed back into the rows the index does have.
            var unnarrowedFilter = Translate(filterTranslator, callerFilter);

            if (!string.Equals(unnarrowedFilter, providerFilter, StringComparison.Ordinal))
            {
                var unnarrowedSets = await SearchIndexAsync(
                    contentManager,
                    masterIndexProfile,
                    vectors,
                    request.DataSourceId,
                    candidateCount,
                    unnarrowedFilter,
                    cancellationToken);

                if (!IsEmpty(unnarrowedSets))
                {
                    resultSets = unnarrowedSets;
                    contentTypeFilterDropped = true;

                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation(
                            "Knowledge base index '{IndexProfileName}' could not filter by content type, so AI tool '{ToolName}' searched it without that narrowing.",
                            masterIndexProfile.Name,
                            toolName);
                    }
                }
            }
        }

        if (IsEmpty(resultSets))
        {
            return Message(BuildEmptyResultMessage(
                "No relevant content was found in the data source for this query.",
                request.IsInScope));
        }

        var minimumScore = siteSettings.GetMinimumScore(request.Strictness);
        var selected = DataSourceSearchResultSelector.FuseTopResults(resultSets, topN, minimumScore);

        if (selected.Count == 0)
        {
            return Message(BuildEmptyResultMessage(
                "No results met the strictness and quality thresholds.",
                request.IsInScope));
        }

        var textNormalizer = services.GetRequiredService<IAITextNormalizer>();

        // Read once and shared: citations and figures register their markers on the same context, so the two
        // cannot disagree about which invocation they belong to.
        var invocationContext = AIInvocationScope.Current;
        var references = new ReferenceCollector(invocationContext);

        using var builder = ZString.CreateStringBuilder();

        if (contentTypeFilterDropped)
        {
            // Said before the content, because a model that asked for figures and is handed prose would
            // otherwise read the prose as the answer to the question it asked.
            builder.AppendLine(BuildUnnarrowedNotice(request.ContentTypes));
        }

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

        var typed = new TypedResultCollector(services, request.DataSourceId, dataSource.Source, invocationContext, logger);

        typed.Collect(selected);

        builder.Append(typed.Render());
        builder.Append(references.Render());

        return new DataSourceRetrievalResult
        {
            Text = builder.ToString(),
            Figures = typed.Figures,
            Tables = typed.Tables,
        };
    }

    /// <summary>
    /// Wraps a plain message as a result, so every exit from a search returns the same shape.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <returns>The result.</returns>
    private static DataSourceRetrievalResult Message(string text)
    {
        return new DataSourceRetrievalResult
        {
            Text = text,
        };
    }

    /// <summary>
    /// Builds the clause that narrows a search to the kinds of knowledge the caller asked for.
    /// </summary>
    /// <param name="contentTypes">The kinds of knowledge to search, when the caller named any.</param>
    /// <returns>The clause, or <see langword="null"/> when the caller named none.</returns>
    /// <remarks>
    /// A row written before typed knowledge existed has no <c>contentType</c>, so asking for text alone also
    /// asks for rows that have no value. Anything else would hide every document indexed before the column
    /// existed.
    /// </remarks>
    private static string BuildContentTypeClause(IReadOnlyList<string> contentTypes)
    {
        if (contentTypes is not { Count: > 0 })
        {
            return null;
        }

        var clauses = contentTypes
            .Where(contentType => !string.IsNullOrWhiteSpace(contentType))
            .Select(contentType => $"{DataSourceConstants.ColumnNames.ContentType} eq '{contentType.Replace("'", "''", StringComparison.Ordinal)}'")
            .ToList();

        if (clauses.Count == 0)
        {
            return null;
        }

        if (contentTypes.Any(contentType => string.Equals(contentType, KnowledgeContentTypes.Text, StringComparison.OrdinalIgnoreCase)))
        {
            clauses.Add($"{DataSourceConstants.ColumnNames.ContentType} eq null");
        }

        return clauses.Count == 1 ? clauses[0] : $"({string.Join(" or ", clauses)})";
    }

    /// <summary>
    /// Joins the caller's filter and the content-type clause into the one filter to translate.
    /// </summary>
    /// <param name="filter">The caller's OData filter, when it has one.</param>
    /// <param name="clause">The content-type clause, when there is one.</param>
    /// <returns>The filter to translate, or <see langword="null"/> when there is nothing to filter on.</returns>
    private static string CombineFilters(string filter, string clause)
    {
        if (string.IsNullOrWhiteSpace(clause))
        {
            return filter;
        }

        return string.IsNullOrWhiteSpace(filter) ? clause : $"({filter}) and {clause}";
    }

    /// <summary>
    /// Translates a filter into the provider's own syntax, when there is a filter and a translator for it.
    /// </summary>
    /// <param name="translator">The provider's filter translator, when one is registered.</param>
    /// <param name="filter">The OData filter, when there is one.</param>
    /// <returns>The provider filter, or <see langword="null"/> when nothing is to be filtered.</returns>
    private static string Translate(IODataFilterTranslator translator, string filter)
    {
        if (translator == null || string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        return translator.Translate(filter);
    }

    /// <summary>
    /// Runs every phrase's vector against the index.
    /// </summary>
    /// <param name="contentManager">The provider's vector search service.</param>
    /// <param name="indexProfile">The knowledge base index profile.</param>
    /// <param name="vectors">The query vectors, one per phrase.</param>
    /// <param name="dataSourceId">The data source to search within.</param>
    /// <param name="candidateCount">The number of candidates to ask each query for.</param>
    /// <param name="providerFilter">The provider filter, when there is one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One result set per phrase.</returns>
    /// <remarks>
    /// The phrases are independent queries against a thread-safe index client, so they run together rather
    /// than one after another. That matters on a realtime session, where a grounded turn cannot start
    /// speaking until retrieval returns.
    /// </remarks>
    private static async Task<IEnumerable<DataSourceSearchResult>[]> SearchIndexAsync(
        IDataSourceContentManager contentManager,
        SearchIndexProfile indexProfile,
        IReadOnlyList<float[]> vectors,
        string dataSourceId,
        int candidateCount,
        string providerFilter,
        CancellationToken cancellationToken)
    {
        return await Task.WhenAll(vectors.Select(vector => contentManager.SearchAsync(
            indexProfile,
            vector,
            dataSourceId,
            candidateCount,
            providerFilter,
            cancellationToken)));
    }

    /// <summary>
    /// Determines whether a search found nothing at all.
    /// </summary>
    /// <param name="resultSets">The result sets, one per phrase.</param>
    /// <returns><see langword="true"/> when no phrase matched anything.</returns>
    private static bool IsEmpty(IEnumerable<DataSourceSearchResult>[] resultSets)
    {
        return resultSets.All(resultSet => resultSet == null || !resultSet.Any());
    }

    /// <summary>
    /// Builds the line that states the search was not narrowed after all.
    /// </summary>
    /// <param name="contentTypes">The kinds of knowledge the caller asked for.</param>
    /// <returns>The line to render above the content.</returns>
    /// <remarks>
    /// Returning the rows an older index does have is only an improvement if the answer says the narrowing
    /// did not happen. Silently widening a search the caller asked to narrow is how a model ends up quoting
    /// a paragraph as though it were the figure it asked for.
    /// </remarks>
    private static string BuildUnnarrowedNotice(IReadOnlyList<string> contentTypes)
    {
        var requested = string.Join(
            ", ",
            contentTypes.Where(contentType => !string.IsNullOrWhiteSpace(contentType)).Select(contentType => contentType.Trim()));

        return $"This knowledge base index cannot filter by content type, so the results below were not narrowed to {requested} and may hold any kind of content.";
    }

    /// <summary>
    /// Addresses the caller's own filter fields through the per-row filter bag, so a field that happens to
    /// share one of the reserved names keeps meaning the caller's field.
    /// </summary>
    /// <param name="filter">The caller's OData filter, when it has one.</param>
    /// <returns>The filter with its reserved names qualified.</returns>
    /// <remarks>
    /// The typed columns belong to ingested knowledge. A data source whose own documents have carried a
    /// top-level <c>contentType</c> since long before those columns existed would otherwise have every
    /// filter on it translated into a lookup against a column its rows never fill, and match nothing.
    /// </remarks>
    private static string QualifyCallerFields(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return filter;
        }

        return FilterTokenRegex().Replace(filter, match =>
        {
            var token = match.Value;

            // A quoted literal is a value and never a field, and a name that is not reserved already means
            // what the caller wrote.
            if (token[0] == '\'' || !DataSourceConstants.ColumnNames.IsReservedColumnName(token))
            {
                return token;
            }

            return IsFieldPosition(filter, match.Index + match.Length)
                ? DataSourceConstants.ColumnNames.QualifyFilterField(token)
                : token;
        });
    }

    /// <summary>
    /// Determines whether the name ending at the supplied position was written where a field belongs.
    /// </summary>
    /// <param name="filter">The filter being read.</param>
    /// <param name="index">The position just past the name.</param>
    /// <returns><see langword="true"/> when a field belongs there.</returns>
    /// <remarks>
    /// A comparison names its field on the left and quotes its value on the right, and a function names its
    /// field before the comma. Nothing else in the grammar is a field, so nothing else is rewritten.
    /// </remarks>
    private static bool IsFieldPosition(string filter, int index)
    {
        while (index < filter.Length && char.IsWhiteSpace(filter[index]))
        {
            index++;
        }

        if (index >= filter.Length)
        {
            return false;
        }

        if (filter[index] == ',')
        {
            return true;
        }

        var start = index;

        while (index < filter.Length && char.IsLetter(filter[index]))
        {
            index++;
        }

        return IsComparisonOperator(filter.AsSpan(start, index - start));
    }

    /// <summary>
    /// Determines whether the supplied word is one of the OData comparison operators.
    /// </summary>
    /// <param name="word">The word read after a name.</param>
    /// <returns><see langword="true"/> when the word compares.</returns>
    private static bool IsComparisonOperator(ReadOnlySpan<char> word)
    {
        return word.Equals("eq", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("ne", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("gt", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("ge", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("lt", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("le", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"'[^']*'|\w[\w.]*")]
    private static partial Regex FilterTokenRegex();

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

        foreach (var result in GroupByDocument(results))
        {
            if (string.IsNullOrWhiteSpace(result.Content))
            {
                continue;
            }

            var entry = references.Track(result.ReferenceId, ResolveCitationTitle(textNormalizer, result, results), result.ReferenceType, result.DataSourceId);

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
                DataSourceId = group.Select(result => result.DataSourceId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id)),
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
            var entry = references.Track(group.ReferenceId, title, group.ReferenceType, group.DataSourceId);

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
    /// Orders the hits so everything from one document renders together, and everything from one article
    /// within it renders adjacently.
    /// </summary>
    /// <param name="results">The selected search results, in score order.</param>
    /// <returns>The results, regrouped.</returns>
    /// <remarks>
    /// A figure and the paragraph that cites it are two hits with two scores, and interleaving them with
    /// hits from another document makes the model read them as unrelated. Score order is preserved within a
    /// group and between groups, so nothing is promoted by grouping alone.
    /// </remarks>
    private static IEnumerable<DataSourceSearchResult> GroupByDocument(IReadOnlyList<DataSourceSearchResult> results)
    {
        if (results.All(result => string.IsNullOrEmpty(result.RootId)))
        {
            return results;
        }

        return results
            .Select((result, index) => (result, index))
            .GroupBy(item => item.result.RootId ?? item.result.ReferenceId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Min(item => item.index))
            .SelectMany(group => group
                .GroupBy(item => item.result.ParentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .OrderBy(articleGroup => articleGroup.Min(item => item.index))
                .SelectMany(articleGroup => articleGroup.OrderBy(item => item.index))
                .Select(item => item.result));
    }

    /// <summary>
    /// Resolves the title a hit is cited under. A figure's own title is its caption, which reads as a
    /// citation of the caption rather than of the document, so the document's title is preferred and the
    /// page is named alongside it.
    /// </summary>
    /// <param name="textNormalizer">The text normalizer used to clean citation titles.</param>
    /// <param name="result">The hit.</param>
    /// <param name="results">Every selected hit, used to find the document title.</param>
    /// <returns>The citation title.</returns>
    private static string ResolveCitationTitle(
        IAITextNormalizer textNormalizer,
        DataSourceSearchResult result,
        IReadOnlyList<DataSourceSearchResult> results)
    {
        var title = result.Title;

        if (!string.IsNullOrEmpty(result.RootId))
        {
            var documentTitle = results
                .Where(item => string.Equals(item.RootId, result.RootId, StringComparison.OrdinalIgnoreCase))
                .Where(item => item.ContentType is null or KnowledgeContentTypes.Text or KnowledgeContentTypes.Article or KnowledgeContentTypes.Document)
                .Select(item => item.Title)
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));

            if (!string.IsNullOrWhiteSpace(documentTitle))
            {
                title = documentTitle;
            }
        }

        title = ResolveReferenceTitle(textNormalizer, title, result.ReferenceId);

        return result.Page.HasValue ? $"{title}, p. {result.Page.Value}" : title;
    }

    /// <summary>
    /// Assigns one citation index per source document and renders the trailing reference list, registering
    /// each citation on the active invocation context so the UI can turn it into a link.
    /// </summary>
    private sealed class ReferenceCollector
    {
        private readonly Dictionary<string, (int Index, string Title, string ReferenceType, string DataSourceId)> _seen = new(StringComparer.OrdinalIgnoreCase);
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
        /// <param name="dataSourceId">The data source the result came from.</param>
        public (string Label, string Title) Track(string referenceId, string title, string referenceType, string dataSourceId)
        {
            if (string.IsNullOrEmpty(referenceId))
            {
                return ($"[doc:{NextIndex()}]", title);
            }

            if (!_seen.TryGetValue(referenceId, out var entry))
            {
                entry = (NextIndex(), title, referenceType, dataSourceId);
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
                        DataSourceId = kvp.Value.DataSourceId,
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
