using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.FileSources;

/// <summary>
/// Schema builder for the <see cref="FileSourceIndex"/> and <see cref="IngestionItemStateIndex"/> tables.
/// </summary>
public static class FileSourceIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates the file-source index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateFileSourceIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<FileSourceIndex>(table => table
            .Column<string>(nameof(FileSourceIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(FileSourceIndex.DisplayText), column => column.WithLength(255))
            .Column<string>(nameof(FileSourceIndex.AIDataSourceId), column => column.WithLength(26))
            .Column<string>(nameof(FileSourceIndex.Source), column => column.WithLength(128)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<FileSourceIndex>(table => table
            .CreateIndex("IDX_FileSource_DataSourceId", "DocumentId", nameof(FileSourceIndex.AIDataSourceId)),
            collection: options?.AICollectionName);
    }

    /// <summary>
    /// Creates the ingestion item-state index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateIngestionItemStateIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<IngestionItemStateIndex>(table => table
            .Column<string>(nameof(IngestionItemStateIndex.ItemId), column => column.WithLength(26))
            .Column<string>(nameof(IngestionItemStateIndex.Source), column => column.WithLength(26))
            .Column<string>(nameof(IngestionItemStateIndex.ItemKey), column => column.WithLength(2048)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<IngestionItemStateIndex>(table => table
            .CreateIndex("IDX_IngestionItemState_Source", "DocumentId", nameof(IngestionItemStateIndex.Source)),
            collection: options?.AICollectionName);
    }
}
