using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;
public static class AIDeploymentIndexSchemaBuilderExtensions
{
    public static Task CreateAIDeploymentIndexSchemaAsync(this ISchemaBuilder schemaBuilder)
    {
        return schemaBuilder.CreateMapIndexTableAsync<AIDeploymentIndex>(table => table.Column<string>(nameof(AIDeploymentIndex.ItemId), column => column.WithLength(26)).Column<string>(nameof(AIDeploymentIndex.Name), column => column.WithLength(255)).Column<string>(nameof(AIDeploymentIndex.Source), column => column.WithLength(255)));
    }
}