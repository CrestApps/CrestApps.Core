using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Services;

internal sealed class AIDataSourceIndexingWorkItem
{
    public AIDataSource DataSource { get; private init; }

    public string DataSourceId { get; private init; }

    public IReadOnlyCollection<string> DocumentIds { get; private init; } = [];

    public string SourceIndexProfileName { get; private init; }

    public AIDataSourceIndexingWorkItemType Type { get; private init; }

    /// <summary>
    /// For sync data sources sync data source.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    public static AIDataSourceIndexingWorkItem ForSyncDataSource(AIDataSource dataSource)
    {
        return new()
        {
            DataSource = dataSource,
            Type = AIDataSourceIndexingWorkItemType.SyncDataSource,
        };
    }

    /// <summary>
    /// Fors delete data source.
    /// </summary>
    /// <param name="dataSource">The data source.</param>
    public static AIDataSourceIndexingWorkItem ForDeleteDataSource(AIDataSource dataSource)
    {
        return new()
        {
            DataSource = dataSource,
            Type = AIDataSourceIndexingWorkItemType.DeleteDataSource,
        };
    }

    /// <summary>
    /// Fors sync source documents.
    /// </summary>
    /// <param name="sourceIndexProfileName">The source index profile name.</param>
    /// <param name="documentIds">The document ids.</param>
    public static AIDataSourceIndexingWorkItem ForSyncSourceDocuments(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds)
    {
        return new()
        {
            DocumentIds = documentIds,
            SourceIndexProfileName = sourceIndexProfileName,
            Type = AIDataSourceIndexingWorkItemType.SyncSourceDocuments,
        };
    }

    /// <summary>
    /// Fors remove source documents.
    /// </summary>
    /// <param name="sourceIndexProfileName">The source index profile name.</param>
    /// <param name="documentIds">The document ids.</param>
    public static AIDataSourceIndexingWorkItem ForRemoveSourceDocuments(string sourceIndexProfileName, IReadOnlyCollection<string> documentIds)
    {
        return new()
        {
            DocumentIds = documentIds,
            SourceIndexProfileName = sourceIndexProfileName,
            Type = AIDataSourceIndexingWorkItemType.RemoveSourceDocuments,
        };
    }

    /// <summary>
    /// Fors sync data source documents.
    /// </summary>
    /// <param name="dataSourceId">The data source id.</param>
    /// <param name="documentIds">The document ids.</param>
    public static AIDataSourceIndexingWorkItem ForSyncDataSourceDocuments(string dataSourceId, IReadOnlyCollection<string> documentIds)
    {
        return new()
        {
            DataSourceId = dataSourceId,
            DocumentIds = documentIds,
            Type = AIDataSourceIndexingWorkItemType.SyncDataSourceDocuments,
        };
    }

    /// <summary>
    /// Fors remove data source documents.
    /// </summary>
    /// <param name="dataSourceId">The data source id.</param>
    /// <param name="documentIds">The document ids.</param>
    public static AIDataSourceIndexingWorkItem ForRemoveDataSourceDocuments(string dataSourceId, IReadOnlyCollection<string> documentIds)
    {
        return new()
        {
            DataSourceId = dataSourceId,
            DocumentIds = documentIds,
            Type = AIDataSourceIndexingWorkItemType.RemoveDataSourceDocuments,
        };
    }
}
