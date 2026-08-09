using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.Mcp;

/// <summary>
/// Schema builder extensions for the <see cref="DocumentationSourceEntryIndex"/> table.
/// </summary>
public static class DocumentationSourceEntryIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates the documentation source entry index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateDocumentationSourceEntryIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<DocumentationSourceEntryIndex>(table => table
            .Column<string>(nameof(DocumentationSourceEntryIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(DocumentationSourceEntryIndex.Name), column => column.WithLength(255))
            .Column<string>(nameof(DocumentationSourceEntryIndex.Source), column => column.WithLength(255)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<DocumentationSourceEntryIndex>(
            table => table.CreateIndex("IDX_DocumentationSourceEntry_DocumentId", "DocumentId", nameof(DocumentationSourceEntryIndex.Name)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<DocumentationSourceEntryIndex>(
            table => table.CreateIndex("IDX_DocumentationSourceEntry_Source", "DocumentId", nameof(DocumentationSourceEntryIndex.Source)),
            collection: options?.AICollectionName);
    }
}
