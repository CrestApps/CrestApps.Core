using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Support;
using CrestApps.Core.Templates.Services;
using Cysharp.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Handlers;

internal sealed class DataSourcePreemptiveRagHandler : IPreemptiveRagHandler
{
    /// <summary>
    /// What the model is told when the knowledge base could not be searched.
    /// </summary>
    /// <remarks>
    /// Injecting nothing is indistinguishable from a knowledge base that holds nothing on the subject, and a
    /// model given no context answers from its own knowledge as though it had checked the data source. It has
    /// to be told that the check never happened.
    /// </remarks>
    private const string SearchFailedNotice =
        "The knowledge base for this data source could not be searched, so any content below may be incomplete or missing entirely. Tell the user the knowledge base is unavailable rather than answering as though the data source has no such content.";

    /// <summary>
    /// The orchestration property that records that this turn's knowledge base could not be searched, for
    /// the handlers that run after this one.
    /// </summary>
    private const string DataSourceSearchFailedKey = "DataSourceSearchFailed";

    private readonly IServiceProvider _serviceProvider;
    private readonly IAIClientFactory _aiClientFactory;
    private readonly ITemplateService _templateService;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAITextNormalizer _textNormalizer;
    private readonly AIDataSourceOptions _options;
    private readonly ILogger<DataSourcePreemptiveRagHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSourcePreemptiveRagHandler"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="aiClientFactory">The ai client factory.</param>
    /// <param name="templateService">The template service.</param>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="textNormalizer">The text normalizer.</param>
    /// <param name="options">The options.</param>
    /// <param name="logger">The logger.</param>
    public DataSourcePreemptiveRagHandler(
        IServiceProvider serviceProvider,
        IAIClientFactory aiClientFactory,
        ITemplateService templateService,
        IAIDeploymentManager deploymentManager,
        IAITextNormalizer textNormalizer,
        IOptionsMonitor<AIDataSourceOptions> options,
        ILogger<DataSourcePreemptiveRagHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _aiClientFactory = aiClientFactory;
        _templateService = templateService;
        _deploymentManager = deploymentManager;
        _textNormalizer = textNormalizer;
        _options = options.CurrentValue;
        _logger = logger;
    }

    /// <summary>
    /// Determines whether handle.
    /// </summary>
    /// <param name="context">The context.</param>
    public ValueTask<bool> CanHandleAsync(OrchestrationContextBuiltContext context)
    {
        if (context.OrchestrationContext.CompletionContext == null ||
            string.IsNullOrEmpty(context.OrchestrationContext.CompletionContext.DataSourceId))
        {
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(
            _serviceProvider.GetService<IAIDataSourceStore>() != null &&
            _serviceProvider.GetService<ISearchIndexProfileStore>() != null);
    }

    /// <summary>
    /// Handles the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public async Task HandleAsync(PreemptiveRagContext context)
    {
        var ragMetadata = GetRagMetadata(context.Resource);

        try
        {
            await InjectPreemptiveRagContextAsync(context, ragMetadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during preemptive RAG injection for data source '{DataSourceId}'.",
                context.OrchestrationContext.CompletionContext.DataSourceId);
        }
    }

    private async Task InjectPreemptiveRagContextAsync(PreemptiveRagContext context, AIDataSourceRagMetadata ragMetadata)
    {
        var dataSourceCatalog = _serviceProvider.GetService<IAIDataSourceStore>();
        var indexProfileStore = _serviceProvider.GetService<ISearchIndexProfileStore>();

        if (dataSourceCatalog == null || indexProfileStore == null)
        {
            return;
        }

        var orchestrationContext = context.OrchestrationContext;
        var dataSourceId = orchestrationContext.CompletionContext.DataSourceId;
        var dataSource = await dataSourceCatalog.FindByIdAsync(dataSourceId);

        if (dataSource == null || string.IsNullOrEmpty(dataSource.AIKnowledgeBaseIndexProfileName))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Data source with ID '{DataSourceId}' not found or does not have an associated index profile name.",
                    dataSourceId);
            }

            return;
        }

        var indexProfile = await indexProfileStore.FindByNameAsync(dataSource.AIKnowledgeBaseIndexProfileName);

        if (indexProfile == null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Index profile with name '{IndexProfileName}' not found.",
                    dataSource.AIKnowledgeBaseIndexProfileName);
            }

            return;
        }

        var contentManager = _serviceProvider.GetKeyedService<IDataSourceContentManager>(indexProfile.ProviderName);

        if (contentManager == null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Content manager for provider '{ProviderName}' not found.",
                    indexProfile.ProviderName);
            }

            return;
        }

        var deploymentName = indexProfile.EmbeddingDeploymentName;

        if (indexProfile.TryGet<DataSourceIndexProfileMetadata>(out var profileMetadata) && !string.IsNullOrEmpty(profileMetadata.EmbeddingDeploymentName))
        {
            deploymentName = profileMetadata.EmbeddingDeploymentName;
        }

        if (string.IsNullOrEmpty(deploymentName))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Unable to retrieve deployment name for index profile '{IndexProfileName}'.", indexProfile.Name);
            }

            return;
        }

        var deployment = await _deploymentManager.FindByNameAsync(deploymentName);

        var embeddingGenerator = deployment == null
            ? null
            : await _aiClientFactory.CreateEmbeddingGeneratorAsync(deployment);

        if (embeddingGenerator == null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Unable to create embedding generator for provider '{ProviderName}'.",
                    indexProfile.ProviderName);
            }

            return;
        }

        await SearchAndInjectContextAsync(context, ragMetadata, dataSource, indexProfile, contentManager, embeddingGenerator);
    }

    private async Task SearchAndInjectContextAsync(
        PreemptiveRagContext context,
        AIDataSourceRagMetadata ragMetadata,
        AIDataSource dataSource,
        SearchIndexProfile indexProfile,
        IDataSourceContentManager contentManager,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        var orchestrationContext = context.OrchestrationContext;
        var dataSourceId = orchestrationContext.CompletionContext.DataSourceId;
        var searchQueries = GetSearchQueries(orchestrationContext.UserMessage, context.Queries);

        if (searchQueries.Count == 0)
        {
            return;
        }

        var embeddings = await embeddingGenerator.GenerateAsync(searchQueries);

        if (embeddings == null || embeddings.Count == 0)
        {
            return;
        }

        var topN = _options.GetTopNDocuments(ragMetadata?.TopNDocuments);

        string providerFilter = null;

        if (!string.IsNullOrWhiteSpace(ragMetadata?.Filter))
        {
            var filterTranslator = _serviceProvider.GetKeyedService<IODataFilterTranslator>(indexProfile.ProviderName);

            if (filterTranslator != null)
            {
                providerFilter = filterTranslator.Translate(ragMetadata.Filter);
            }
        }

        var minimumScore = _options.GetMinimumScore(ragMetadata?.Strictness);
        var finalResults = new List<DataSourceSearchResult>();
        var seenChunkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidateCount = DataSourceSearchResultSelector.GetCandidateCount(topN);
        var searchFailed = false;

        foreach (var embedding in embeddings)
        {
            if (embedding?.Vector == null)
            {
                continue;
            }

            var outcome = await contentManager.SearchWithOutcomeAsync(
                indexProfile,
                embedding.Vector.ToArray(),
                dataSourceId,
                candidateCount,
                providerFilter);

            if (!outcome.Succeeded)
            {
                // Remembered rather than passed over. A search that could not run injects the same nothing
                // as a search that matched nothing, and a model given no context answers from its own
                // knowledge as though it had checked the data source and found it wanting.
                searchFailed = true;

                continue;
            }

            foreach (var result in DataSourceSearchResultSelector.SelectTopResults(outcome.Results, candidateCount, minimumScore))
            {
                var chunkKey = $"{result.ReferenceId}:{result.ChunkIndex}";

                if (seenChunkIds.Add(chunkKey))
                {
                    finalResults.Add(result);

                    if (finalResults.Count >= topN)
                    {
                        break;
                    }
                }
            }

            if (finalResults.Count >= topN)
            {
                break;
            }
        }

        await AddRelevantPicturesAsync(
            contentManager,
            _serviceProvider.GetKeyedService<IODataFilterTranslator>(indexProfile.ProviderName),
            indexProfile,
            embeddings,
            dataSourceId,
            candidateCount,
            minimumScore,
            seenChunkIds,
            finalResults,
            _logger);

        if (finalResults.Count == 0 && !searchFailed)
        {
            return;
        }

        using var stringBuilder = ZString.CreateStringBuilder();

        var templateArguments = new Dictionary<string, object>();

        if (!orchestrationContext.DisableTools)
        {
            templateArguments["searchToolName"] = SystemToolNames.SearchDataSources;
        }

        var header = await _templateService.RenderAsync(AITemplateIds.DataSourceContextHeader, templateArguments);

        if (!string.IsNullOrEmpty(header))
        {
            stringBuilder.AppendLine();
            stringBuilder.AppendLine();
            stringBuilder.Append(header);
        }

        if (searchFailed)
        {
            stringBuilder.AppendLine();
            stringBuilder.AppendLine();
            stringBuilder.AppendLine(SearchFailedNotice);

            // Recorded for whatever else builds this turn's system message. A handler that sees no references
            // and concludes the knowledge sources hold nothing has the same question to answer as this one
            // did, and this is the only place that knows the answer.
            orchestrationContext.Properties[DataSourceSearchFailedKey] = true;

            _logger.LogWarning(
                "The knowledge base index '{IndexProfileName}' could not be searched for data source '{DataSourceId}'. The model is told so rather than being left to answer as though the data source were empty.",
                indexProfile.Name,
                dataSourceId);
        }

        var invocationContext = AIInvocationScope.Current;
        var seenReferences = new Dictionary<string, (int Index, string Title, string ReferenceType, string DataSourceId)>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in finalResults)
        {
            if (string.IsNullOrWhiteSpace(result.Content))
            {
                continue;
            }

            var hasReference = !string.IsNullOrEmpty(result.ReferenceId);

            if (hasReference && !seenReferences.ContainsKey(result.ReferenceId))
            {
                seenReferences[result.ReferenceId] = (
                    invocationContext?.NextReferenceIndex() ?? seenReferences.Count + 1,
                    ResolveReferenceTitle(result.Title, result.ReferenceId),
                    result.ReferenceType,
                    result.DataSourceId);
            }

            var referenceIndex = hasReference && seenReferences.TryGetValue(result.ReferenceId, out var entry)
                ? entry.Index
                : invocationContext?.NextReferenceIndex() ?? seenReferences.Count + 1;

            stringBuilder.AppendLine("---");
            stringBuilder.Append("[doc:");
            stringBuilder.Append(referenceIndex);
            stringBuilder.Append("] ");
            stringBuilder.AppendLine(result.Content);
        }

        if (seenReferences.Count > 0)
        {
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("References:");

            var citationMap = new Dictionary<string, AICompletionReference>();

            foreach (var (referenceId, value) in seenReferences)
            {
                stringBuilder.Append("[doc:");
                stringBuilder.Append(value.Index);
                stringBuilder.Append("] = {ReferenceId: \"");
                stringBuilder.Append(referenceId);
                stringBuilder.Append('"');

                if (!string.IsNullOrWhiteSpace(value.Title))
                {
                    stringBuilder.Append(", Title: \"");
                    stringBuilder.Append(value.Title);
                    stringBuilder.Append('"');
                }

                stringBuilder.AppendLine("}");

                var template = $"[doc:{value.Index}]";
                citationMap[template] = new AICompletionReference
                {
                    Text = string.IsNullOrWhiteSpace(value.Title) ? template : value.Title,
                    Title = value.Title,
                    Index = value.Index,
                    ReferenceId = referenceId,
                    ReferenceType = value.ReferenceType,
                    DataSourceId = value.DataSourceId,
                };
            }

            orchestrationContext.Properties["DataSourceReferences"] = citationMap;
        }

        // Figures and tables are named the same way the tool path names them, through the same collector.
        // Without this a data source attached to a chat interaction retrieves a figure's text and then has
        // no way to show the picture, because nothing ever hands the model a [fig:N] label to write.
        var typed = new TypedResultCollector(
            _serviceProvider,
            dataSourceId,
            dataSource?.Source,
            invocationContext,
            _logger);

        typed.Collect(finalResults);

        var typedBlocks = typed.Render();

        if (!string.IsNullOrEmpty(typedBlocks))
        {
            stringBuilder.Append(typedBlocks);
        }

        orchestrationContext.SystemMessageBuilder.Append(stringBuilder);
    }

    /// <summary>
    /// The most pictures one turn adds. A picture the reader did not ask for costs them nothing to ignore,
    /// but a system message full of them crowds out the prose that actually answers the question.
    /// </summary>
    private const int MaxPreemptivePictures = 2;

    /// <summary>
    /// Adds the figures most relevant to the question, searched for on their own.
    /// </summary>
    /// <remarks>
    /// A picture only reaches the reader if retrieval returned it, and on a plain search it usually does not:
    /// "show me a figure about glazing" embeds as glazing, and the prose about glazing outscores the pictures
    /// of it. The wish for a picture is in the question and not in the vector, so no amount of ranking finds
    /// it. Searching the pictures separately gives them their own contest to win, and the same score floor
    /// still applies, so a question no picture suits adds none.
    /// </remarks>
    private static async Task AddRelevantPicturesAsync(
        IDataSourceContentManager contentManager,
        IODataFilterTranslator filterTranslator,
        SearchIndexProfile indexProfile,
        IReadOnlyList<Embedding<float>> embeddings,
        string dataSourceId,
        int candidateCount,
        float minimumScore,
        HashSet<string> seenChunkIds,
        List<DataSourceSearchResult> finalResults,
        ILogger logger)
    {
        // The content manager takes a provider-native filter, not the OData the clause is written in, so a
        // provider with no translator registered cannot be asked for pictures at all.
        if (filterTranslator is null)
        {
            return;
        }

        var clause = $"({DataSourceConstants.ColumnNames.ContentType} eq '{KnowledgeContentTypes.Figure}' or {DataSourceConstants.ColumnNames.ContentType} eq '{KnowledgeContentTypes.Chart}')";
        var pictureFilter = filterTranslator.Translate(clause);

        if (string.IsNullOrEmpty(pictureFilter))
        {
            return;
        }

        var added = 0;

        foreach (var embedding in embeddings)
        {
            if (added >= MaxPreemptivePictures || embedding?.Vector == null)
            {
                break;
            }

            var outcome = await contentManager.SearchWithOutcomeAsync(
                indexProfile,
                embedding.Vector.ToArray(),
                dataSourceId,
                candidateCount,
                pictureFilter);

            if (!outcome.Succeeded)
            {
                // An index predating the typed columns cannot honour this filter. The prose already
                // retrieved stands on its own, so the turn continues without pictures -- but it says so,
                // because a picture that silently never arrives is indistinguishable from one that does not
                // exist.
                logger.LogWarning("Could not search for pictures in index '{IndexName}'. The answer will have none.", indexProfile.Name);

                return;
            }

            foreach (var result in DataSourceSearchResultSelector.SelectTopResults(outcome.Results, candidateCount, minimumScore))
            {
                if (added >= MaxPreemptivePictures)
                {
                    break;
                }

                if (seenChunkIds.Add($"{result.ReferenceId}:{result.ChunkIndex}"))
                {
                    finalResults.Add(result);
                    added++;
                }
            }
        }
    }

    /// <summary>
    /// Resolves a citation title that never exposes a serialized source document.
    /// </summary>
    /// <param name="title">The indexed document title.</param>
    /// <param name="referenceId">The document reference identifier used as the fallback title.</param>
    /// <returns>The resolved citation title.</returns>
    private string ResolveReferenceTitle(string title, string referenceId)
    {
        var normalizedTitle = _textNormalizer.NormalizeTitle(title);

        if (string.IsNullOrWhiteSpace(normalizedTitle) || DocumentTitleResolver.LooksLikeSerializedDocument(normalizedTitle))
        {
            return referenceId;
        }

        return normalizedTitle;
    }

    private static List<string> GetSearchQueries(string userMessage, IList<string> derivedQueries)
    {
        var queries = new List<string>();
        var seenQueries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void AddQuery(ICollection<string> queries, ISet<string> seenQueries, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmedValue = value.Trim();

            if (seenQueries.Add(trimmedValue))
            {
                queries.Add(trimmedValue);
            }
        }

        AddQuery(queries, seenQueries, userMessage);

        if (derivedQueries != null)
        {
            foreach (var query in derivedQueries)
            {
                AddQuery(queries, seenQueries, query);
            }
        }

        return queries;
    }

    private static AIDataSourceRagMetadata GetRagMetadata(object resource)
    {
        if (resource is AIProfile profile &&
            profile.TryGet<AIDataSourceRagMetadata>(out var ragMetadata))
        {
            return ragMetadata;
        }

        if (resource is ChatInteraction interaction &&
            interaction.TryGet<AIDataSourceRagMetadata>(out var interactionRagMetadata))
        {
            return interactionRagMetadata;
        }

        return null;
    }
}
