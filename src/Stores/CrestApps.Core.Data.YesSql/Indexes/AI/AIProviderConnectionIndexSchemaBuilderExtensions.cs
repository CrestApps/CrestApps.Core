using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

public static class AIProviderConnectionIndexSchemaBuilderExtensions
{
    public static Task CreateAIProviderConnectionIndexSchemaAsync(this ISchemaBuilder schemaBuilder, string collection = null)
    {
        return schemaBuilder.CreateMapIndexTableAsync<AIProviderConnectionIndex>(table => table
            .Column<string>(nameof(AIProviderConnectionIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIProviderConnectionIndex.Name), column => column.WithLength(255))
            .Column<string>(nameof(AIProviderConnectionIndex.Source), column => column.WithLength(255)),
            collection: collection);
    }
}
