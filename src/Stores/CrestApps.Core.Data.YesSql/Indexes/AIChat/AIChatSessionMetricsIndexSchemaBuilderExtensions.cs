using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

public static class AIChatSessionMetricsIndexSchemaBuilderExtensions
{
    public static async Task CreateAIChatSessionMetricsSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions storeOptions, AIChatSessionMetricsIndexSchemaOptions options = null)
    {
        options = NormalizeOptions(options, storeOptions);
        await schemaBuilder.CreateAIChatSessionMetricsIndexTableAsync(storeOptions, options);
        await schemaBuilder.CreateAIChatSessionMetricsNamedIndexesAsync(storeOptions, options);
    }

    public static Task CreateAIChatSessionMetricsIndexTableAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions storeOptions, AIChatSessionMetricsIndexSchemaOptions options = null)
    {
        options = NormalizeOptions(options, storeOptions);

        return schemaBuilder.CreateMapIndexTableAsync<AIChatSessionMetricsIndex>(table => table
            .Column<string>(nameof(AIChatSessionMetricsIndex.SessionId), column => column.WithLength(options.SessionIdLength))
            .Column<string>(nameof(AIChatSessionMetricsIndex.ProfileId), column => column.WithLength(options.ProfileIdLength))
            .Column<string>(nameof(AIChatSessionMetricsIndex.VisitorId), column => column.WithLength(options.VisitorIdLength))
            .Column<string>(nameof(AIChatSessionMetricsIndex.UserId), column => column.WithLength(options.UserIdLength))
            .Column<bool>(nameof(AIChatSessionMetricsIndex.IsAuthenticated))
            .Column<DateTime>(nameof(AIChatSessionMetricsIndex.SessionStartedUtc))
            .Column<DateTime?>(nameof(AIChatSessionMetricsIndex.SessionEndedUtc))
            .Column<int>(nameof(AIChatSessionMetricsIndex.MessageCount))
            .Column<double>(nameof(AIChatSessionMetricsIndex.HandleTimeSeconds))
            .Column<bool>(nameof(AIChatSessionMetricsIndex.IsResolved))
            .Column<int>(nameof(AIChatSessionMetricsIndex.HourOfDay))
            .Column<int>(nameof(AIChatSessionMetricsIndex.DayOfWeek))
            .Column<int>(nameof(AIChatSessionMetricsIndex.TotalInputTokens), column => column.WithDefault(0))
            .Column<int>(nameof(AIChatSessionMetricsIndex.TotalOutputTokens), column => column.WithDefault(0))
            .Column<double>(nameof(AIChatSessionMetricsIndex.AverageResponseLatencyMs), column => column.WithDefault(0))
            .Column<int>(nameof(AIChatSessionMetricsIndex.CompletionCount), column => column.WithDefault(0))
            .Column<bool?>(nameof(AIChatSessionMetricsIndex.UserRating), column => column.Nullable())
            .Column<int>(nameof(AIChatSessionMetricsIndex.ThumbsUpCount), column => column.WithDefault(0))
            .Column<int>(nameof(AIChatSessionMetricsIndex.ThumbsDownCount), column => column.WithDefault(0))
            .Column<int?>(nameof(AIChatSessionMetricsIndex.ConversionScore), column => column.Nullable())
            .Column<int?>(nameof(AIChatSessionMetricsIndex.ConversionMaxScore), column => column.Nullable())
            .Column<DateTime>(nameof(AIChatSessionMetricsIndex.CreatedUtc)),
            collection: options.CollectionName);
    }

    public static Task CreateAIChatSessionMetricsNamedIndexesAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions storeOptions, AIChatSessionMetricsIndexSchemaOptions options = null)
    {
        options = NormalizeOptions(options, storeOptions);

        if (!options.CreateNamedIndexes)
        {
            return Task.CompletedTask;
        }

        return Task.WhenAll(
            schemaBuilder.AlterIndexTableAsync<AIChatSessionMetricsIndex>(
                table => table.CreateIndex("IDX_AIChatSessionMetrics_DocumentId", "DocumentId", nameof(AIChatSessionMetricsIndex.SessionId), nameof(AIChatSessionMetricsIndex.ProfileId), nameof(AIChatSessionMetricsIndex.CreatedUtc)),
                collection: options.CollectionName),
            schemaBuilder.AlterIndexTableAsync<AIChatSessionMetricsIndex>(
                table => table.CreateIndex("IDX_AIChatSessionMetrics_ProfileDate", "DocumentId", nameof(AIChatSessionMetricsIndex.ProfileId), nameof(AIChatSessionMetricsIndex.SessionStartedUtc), nameof(AIChatSessionMetricsIndex.SessionEndedUtc), nameof(AIChatSessionMetricsIndex.IsResolved)),
                collection: options.CollectionName),
            schemaBuilder.AlterIndexTableAsync<AIChatSessionMetricsIndex>(
                table => table.CreateIndex("IDX_AIChatSessionMetrics_VisitorId", "DocumentId", nameof(AIChatSessionMetricsIndex.VisitorId), nameof(AIChatSessionMetricsIndex.ProfileId), nameof(AIChatSessionMetricsIndex.SessionStartedUtc)),
                collection: options.CollectionName),
            schemaBuilder.AlterIndexTableAsync<AIChatSessionMetricsIndex>(
                table => table.CreateIndex("IDX_AIChatSessionMetrics_TimeOfDay", "DocumentId", nameof(AIChatSessionMetricsIndex.ProfileId), nameof(AIChatSessionMetricsIndex.HourOfDay), nameof(AIChatSessionMetricsIndex.DayOfWeek), nameof(AIChatSessionMetricsIndex.SessionStartedUtc)),
                collection: options.CollectionName));
    }

    public static Task AddAIChatSessionMetricsCompletionCountColumnAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions storeOptions)
    {
        return schemaBuilder.AlterIndexTableAsync<AIChatSessionMetricsIndex>(table =>
        {
            table.AddColumn<int>(nameof(AIChatSessionMetricsIndex.CompletionCount), column => column.WithDefault(0));
        }, collection: storeOptions?.AICollectionName);
    }

    private static AIChatSessionMetricsIndexSchemaOptions NormalizeOptions(AIChatSessionMetricsIndexSchemaOptions options, YesSqlStoreOptions storeOptions)
    {
        options ??= new AIChatSessionMetricsIndexSchemaOptions();

        if (options.CollectionName == null)
        {
            return new AIChatSessionMetricsIndexSchemaOptions
            {
                CollectionName = storeOptions?.AICollectionName,
                SessionIdLength = options.SessionIdLength,
                ProfileIdLength = options.ProfileIdLength,
                VisitorIdLength = options.VisitorIdLength,
                UserIdLength = options.UserIdLength,
                CreateNamedIndexes = options.CreateNamedIndexes,
            };
        }

        return options;
    }
}
