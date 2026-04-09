using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

public static class AIProfileIndexSchemaBuilderExtensions
{
    public static Task CreateAIProfileIndexSchemaAsync(this ISchemaBuilder schemaBuilder)
    {
        return schemaBuilder.CreateMapIndexTableAsync<AIProfileIndex>(table => table.Column<string>(nameof(AIProfileIndex.ItemId), column => column.WithLength(26)).Column<string>(nameof(AIProfileIndex.Name), column => column.WithLength(255)).Column<string>(nameof(AIProfileIndex.Source), column => column.WithLength(255)));
    }
}