using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Knowledge;

/// <summary>
/// Schema builder for the <see cref="KnowledgeObjectIndex"/> table.
/// </summary>
public static class KnowledgeObjectIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates the knowledge object index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateKnowledgeObjectIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<KnowledgeObjectIndex>(table => table
            .Column<string>(nameof(KnowledgeObjectIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(KnowledgeObjectIndex.Source), column => column.WithLength(26))
            .Column<string>(nameof(KnowledgeObjectIndex.CanonicalId), column => column.WithLength(450))
            .Column<string>(nameof(KnowledgeObjectIndex.RootId), column => column.WithLength(450))
            .Column<string>(nameof(KnowledgeObjectIndex.ObjectType), column => column.WithLength(32))
            .Column<string>(nameof(KnowledgeObjectIndex.Status), column => column.WithLength(32))
            .Column<string>(nameof(KnowledgeObjectIndex.ContentHash), column => column.WithLength(64)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<KnowledgeObjectIndex>(table => table
            .CreateIndex("IDX_KnowledgeObject_Source", "DocumentId", nameof(KnowledgeObjectIndex.Source)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<KnowledgeObjectIndex>(table => table
            .CreateIndex("IDX_KnowledgeObject_Root", "DocumentId", nameof(KnowledgeObjectIndex.Source), nameof(KnowledgeObjectIndex.RootId)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<KnowledgeObjectIndex>(table => table
            .CreateIndex("IDX_KnowledgeObject_Hash", "DocumentId", nameof(KnowledgeObjectIndex.ContentHash)),
            collection: options?.AICollectionName);
    }
}
