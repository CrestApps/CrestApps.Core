using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Indexing;

/// <summary>
/// Represents the AI Document Search Index Profile Handler.
/// </summary>
public sealed class AIDocumentSearchIndexProfileHandler : EmbeddingSearchIndexProfileHandlerBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIDocumentSearchIndexProfileHandler"/> class.
    /// </summary>
    /// <param name="deploymentCatalog">The deployment catalog.</param>
    /// <param name="aiClientFactory">The ai client factory.</param>
    /// <param name="logger">The logger.</param>
    public AIDocumentSearchIndexProfileHandler(
        IAIDeploymentStore deploymentCatalog,
        IAIClientFactory aiClientFactory,
        ILogger<AIDocumentSearchIndexProfileHandler> logger)
        : base(IndexProfileTypes.AIDocuments, deploymentCatalog, aiClientFactory, logger)
    {
    }

    /// <summary>
    /// Builds fields.
    /// </summary>
    /// <param name="vectorDimensions">The vector dimensions.</param>
    protected override IReadOnlyCollection<SearchIndexField> BuildFields(int vectorDimensions)
    {
        return
        [
            new SearchIndexField
            {
                Name = DocumentIndexConstants.ColumnNames.ChunkId,
                FieldType = SearchFieldType.Keyword,
                IsKey = true,
                IsFilterable = true,
            }, new SearchIndexField
            {
                Name = DocumentIndexConstants.ColumnNames.DocumentId,
                FieldType = SearchFieldType.Keyword,
                IsFilterable = true,
            }, new SearchIndexField
            {
                Name = DocumentIndexConstants.ColumnNames.Content,
                FieldType = SearchFieldType.Text,
                IsSearchable = true,
            }, new SearchIndexField
            {
                Name = DocumentIndexConstants.ColumnNames.FileName,
                FieldType = SearchFieldType.Keyword,
                IsFilterable = true,
            }, new SearchIndexField
            {
                Name = DocumentIndexConstants.ColumnNames.ReferenceId,
                FieldType = SearchFieldType.Keyword,
                IsFilterable = true,
            }, new SearchIndexField
            {
                Name = DocumentIndexConstants.ColumnNames.ReferenceType,
                FieldType = SearchFieldType.Keyword,
                IsFilterable = true,
            }, new SearchIndexField
            {
                Name = DocumentIndexConstants.ColumnNames.ChunkIndex,
                FieldType = SearchFieldType.Integer,
            }, new SearchIndexField
            {
                Name = DocumentIndexConstants.ColumnNames.Embedding,
                FieldType = SearchFieldType.Vector,
                VectorDimensions = vectorDimensions,
            },
        ];
    }
}
