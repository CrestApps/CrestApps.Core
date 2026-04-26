using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

public static class AIDocumentChunkIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates ai document chunk index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateAIDocumentChunkIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<AIDocumentChunkIndex>(table => table
            .Column<string>(nameof(AIDocumentChunkIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIDocumentChunkIndex.AIDocumentId), column => column.WithLength(26))
            .Column<string>(nameof(AIDocumentChunkIndex.ReferenceId), column => column.WithLength(26))
            .Column<string>(nameof(AIDocumentChunkIndex.ReferenceType), column => column.WithLength(50))
            .Column<int>(nameof(AIDocumentChunkIndex.Index)),
            collection: options?.AIDocsCollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIDocumentChunkIndex>(table =>
        {
            table.CreateIndex("IDX_AIDocumentChunk_DocumentId", "DocumentId", nameof(AIDocumentChunkIndex.AIDocumentId));
            table.CreateIndex("IDX_AIDocumentChunk_Reference", "DocumentId", nameof(AIDocumentChunkIndex.ReferenceId), nameof(AIDocumentChunkIndex.ReferenceType));
        }, collection: options?.AIDocsCollectionName);
    }
}
