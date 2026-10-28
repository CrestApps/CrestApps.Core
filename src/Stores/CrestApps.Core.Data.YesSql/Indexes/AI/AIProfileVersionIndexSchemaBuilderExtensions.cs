using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AI;

/// <summary>
/// Creates the YesSql index table for AI profile versions.
/// </summary>
public static class AIProfileVersionIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates the AI profile version index table and the index a profile's history is read through.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateAIProfileVersionIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);

        await schemaBuilder.CreateMapIndexTableAsync<AIProfileVersionIndex>(table => table
            .Column<string>(nameof(AIProfileVersionIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(AIProfileVersionIndex.ProfileId), column => column.WithLength(26))
            .Column<int>(nameof(AIProfileVersionIndex.Version))
            .Column<DateTime>(nameof(AIProfileVersionIndex.CreatedUtc), column => column.NotNull()),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIProfileVersionIndex>(table =>
        {
            table.CreateIndex(
                "IDX_AIProfileVersion_ProfileId",
                nameof(AIProfileVersionIndex.ProfileId),
                nameof(AIProfileVersionIndex.Version),
                "DocumentId");
        }, collection: options?.AICollectionName);
    }
}
