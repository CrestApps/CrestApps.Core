using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;
public static class AIProfileTemplateIndexSchemaBuilderExtensions
{
    public static Task CreateAIProfileTemplateIndexSchemaAsync(this ISchemaBuilder schemaBuilder)
    {
        return schemaBuilder.CreateMapIndexTableAsync<AIProfileTemplateIndex>(table => table.Column<string>(nameof(AIProfileTemplateIndex.ItemId), column => column.WithLength(26)).Column<string>(nameof(AIProfileTemplateIndex.Name), column => column.WithLength(255)).Column<string>(nameof(AIProfileTemplateIndex.Source), column => column.WithLength(255)));
    }
}