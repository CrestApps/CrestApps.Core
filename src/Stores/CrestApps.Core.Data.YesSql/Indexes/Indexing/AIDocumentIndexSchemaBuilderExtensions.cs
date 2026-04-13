using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Indexing;

public static class AIDocumentIndexSchemaBuilderExtensions
{
    public static Task CreateAIDocumentIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options = null)
    {
        options ??= new YesSqlStoreOptions();

        return schemaBuilder.CreateMapIndexTableAsync<AIDocumentIndex>(table => table
            .Column<string>(nameof(AIDocumentIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIDocumentIndex.ReferenceId), column => column.WithLength(26))
            .Column<string>(nameof(AIDocumentIndex.ReferenceType), column => column.WithLength(50))
            .Column<string>(nameof(AIDocumentIndex.FileName), column => column.WithLength(255)),
            collection: options.AIDocsCollectionName);
    }
}
