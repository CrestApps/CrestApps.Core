using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// Represents the data Source Search Index Profile Handler.
/// </summary>
public sealed class DataSourceSearchIndexProfileHandler : EmbeddingSearchIndexProfileHandlerBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataSourceSearchIndexProfileHandler"/> class.
    /// </summary>
    /// <param name="deploymentCatalog">The deployment catalog.</param>
    /// <param name="aiClientFactory">The ai client factory.</param>
    /// <param name="logger">The logger.</param>
    public DataSourceSearchIndexProfileHandler(
        IAIDeploymentStore deploymentCatalog,
        IAIClientFactory aiClientFactory,
        ILogger<DataSourceSearchIndexProfileHandler> logger)
        : base(IndexProfileTypes.DataSource, deploymentCatalog, aiClientFactory, logger)
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
                Name = DataSourceConstants.ColumnNames.ChunkId,
                FieldType = SearchFieldType.Keyword,
                IsKey = true,
                IsFilterable = true,
            },
            new SearchIndexField
            {
                Name = DataSourceConstants.ColumnNames.ReferenceId,
                FieldType = SearchFieldType.Keyword,
                IsFilterable = true,
            },
            new SearchIndexField
            {
                Name = DataSourceConstants.ColumnNames.DataSourceId,
                FieldType = SearchFieldType.Keyword,
                IsFilterable = true,
            },
            new SearchIndexField
            {
                Name = DataSourceConstants.ColumnNames.ReferenceType,
                FieldType = SearchFieldType.Keyword,
                IsFilterable = true,
            },
            new SearchIndexField
            {
                Name = DataSourceConstants.ColumnNames.ChunkIndex,
                FieldType = SearchFieldType.Integer,
            },
            new SearchIndexField
            {
                Name = DataSourceConstants.ColumnNames.Title,
                FieldType = SearchFieldType.Text,
                IsSearchable = true,
            },
            new SearchIndexField
            {
                Name = DataSourceConstants.ColumnNames.Content,
                FieldType = SearchFieldType.Text,
                IsSearchable = true,
            },
            new SearchIndexField
            {
                Name = DataSourceConstants.ColumnNames.Timestamp,
                FieldType = SearchFieldType.DateTime,
                IsFilterable = true,
            },
            new SearchIndexField
            {
                Name = DataSourceConstants.ColumnNames.Embedding,
                FieldType = SearchFieldType.Vector,
                VectorDimensions = vectorDimensions,
            },
        ];
    }
}
