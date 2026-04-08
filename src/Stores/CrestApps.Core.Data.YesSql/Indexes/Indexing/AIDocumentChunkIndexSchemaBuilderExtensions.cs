using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;
public static class AIDocumentChunkIndexSchemaBuilderExtensions
{
    public static Task CreateAIDocumentChunkIndexSchemaAsync(this ISchemaBuilder schemaBuilder)
    {
        return schemaBuilder.CreateMapIndexTableAsync<AIDocumentChunkIndex>(table => table.Column<string>(nameof(AIDocumentChunkIndex.ItemId), column => column.WithLength(26)).Column<string>(nameof(AIDocumentChunkIndex.AIDocumentId), column => column.WithLength(26)).Column<string>(nameof(AIDocumentChunkIndex.ReferenceId), column => column.WithLength(26)).Column<string>(nameof(AIDocumentChunkIndex.ReferenceType), column => column.WithLength(50)).Column<int>(nameof(AIDocumentChunkIndex.Index)));
    }
}