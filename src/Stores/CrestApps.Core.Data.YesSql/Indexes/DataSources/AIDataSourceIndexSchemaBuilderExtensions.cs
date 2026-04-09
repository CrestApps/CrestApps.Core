using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.DataSources;

public static class AIDataSourceIndexSchemaBuilderExtensions
{
    public static Task CreateAIDataSourceIndexSchemaAsync(this ISchemaBuilder schemaBuilder)
    {
        return schemaBuilder.CreateMapIndexTableAsync<AIDataSourceIndex>(table => table.Column<string>(nameof(AIDataSourceIndex.ItemId), column => column.WithLength(26)).Column<string>(nameof(AIDataSourceIndex.DisplayText), column => column.WithLength(255)).Column<string>(nameof(AIDataSourceIndex.SourceIndexProfileName), column => column.WithLength(255)));
    }
}