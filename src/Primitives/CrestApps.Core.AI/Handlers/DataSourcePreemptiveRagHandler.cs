using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
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

        await SearchAndInjectContextAsync(context, ragMetadata, indexProfile, contentManager, embeddingGenerator);
    }

    private async Task SearchAndInjectContextAsync(
        PreemptiveRagContext context,
        AIDataSourceRagMetadata ragMetadata,
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

        foreach (var embedding in embeddings)
        {
            if (embedding?.Vector == null)
            {
                continue;
            }

            var results = await contentManager.SearchAsync(
                indexProfile,
                embedding.Vector.ToArray(),
                dataSourceId,
                candidateCount,
                providerFilter);

            if (results == null)
            {
                continue;
            }

            foreach (var result in DataSourceSearchResultSelector.SelectTopResults(results, candidateCount, minimumScore))
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

        if (finalResults.Count == 0)
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

        var invocationContext = AIInvocationScope.Current;
        var seenReferences = new Dictionary<string, (int Index, string Title, string ReferenceType)>(StringComparer.OrdinalIgnoreCase);

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
                    result.ReferenceType);
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
                };
            }

            orchestrationContext.Properties["DataSourceReferences"] = citationMap;
        }

        orchestrationContext.SystemMessageBuilder.Append(stringBuilder);
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
