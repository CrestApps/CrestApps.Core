using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

public static class AIChatSessionExtractedDataIndexSchemaBuilderExtensions
{
    /// <summary>
    /// Creates ai chat session extracted data index schema.
    /// </summary>
    /// <param name="schemaBuilder">The schema builder.</param>
    /// <param name="options">The options.</param>
    public static async Task CreateAIChatSessionExtractedDataIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<AIChatSessionExtractedDataIndex>(table => table
            .Column<string>(nameof(AIChatSessionExtractedDataIndex.SessionId), column => column.WithLength(26))
            .Column<string>(nameof(AIChatSessionExtractedDataIndex.ProfileId), column => column.WithLength(26))
            .Column<DateTime>(nameof(AIChatSessionExtractedDataIndex.SessionStartedUtc))
            .Column<DateTime?>(nameof(AIChatSessionExtractedDataIndex.SessionEndedUtc))
            .Column<int>(nameof(AIChatSessionExtractedDataIndex.FieldCount))
            .Column<string>(nameof(AIChatSessionExtractedDataIndex.FieldNames), column => column.WithLength(4000))
            .Column<string>(nameof(AIChatSessionExtractedDataIndex.ValuesText), column => column.WithLength(4000))
            .Column<DateTime>(nameof(AIChatSessionExtractedDataIndex.UpdatedUtc))
            , collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AIChatSessionExtractedDataIndex>(table =>
        {
            table.CreateIndex("IDX_AIChatSessionExtractedData_DocumentId", "DocumentId", nameof(AIChatSessionExtractedDataIndex.SessionId), nameof(AIChatSessionExtractedDataIndex.ProfileId));
        }, collection: options?.AICollectionName);
    }
}
