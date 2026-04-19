using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

public static class AICompletionUsageIndexSchemaBuilderExtensions
{
    public static async Task CreateAICompletionUsageIndexSchemaAsync(this ISchemaBuilder schemaBuilder, YesSqlStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(schemaBuilder);
        ArgumentNullException.ThrowIfNull(options);

        await schemaBuilder.CreateMapIndexTableAsync<AICompletionUsageIndex>(table => table
            .Column<string>(nameof(AICompletionUsageIndex.ContextType), column => column.WithLength(64))
            .Column<string>(nameof(AICompletionUsageIndex.SessionId), column => column.WithLength(26))
            .Column<string>(nameof(AICompletionUsageIndex.ProfileId), column => column.WithLength(26))
            .Column<string>(nameof(AICompletionUsageIndex.InteractionId), column => column.WithLength(26))
            .Column<string>(nameof(AICompletionUsageIndex.UserId), column => column.WithLength(255))
            .Column<string>(nameof(AICompletionUsageIndex.UserName), column => column.WithLength(255))
            .Column<string>(nameof(AICompletionUsageIndex.VisitorId), column => column.WithLength(255))
            .Column<string>(nameof(AICompletionUsageIndex.ClientId), column => column.WithLength(255))
            .Column<bool>(nameof(AICompletionUsageIndex.IsAuthenticated))
            .Column<string>(nameof(AICompletionUsageIndex.ClientName), column => column.WithLength(128))
            .Column<string>(nameof(AICompletionUsageIndex.ConnectionName), column => column.WithLength(255))
            .Column<string>(nameof(AICompletionUsageIndex.DeploymentName), column => column.WithLength(255))
            .Column<string>(nameof(AICompletionUsageIndex.ModelName), column => column.WithLength(255))
            .Column<string>(nameof(AICompletionUsageIndex.ResponseId), column => column.WithLength(255))
            .Column<bool>(nameof(AICompletionUsageIndex.IsStreaming))
            .Column<int>(nameof(AICompletionUsageIndex.InputTokenCount))
            .Column<int>(nameof(AICompletionUsageIndex.OutputTokenCount))
            .Column<int>(nameof(AICompletionUsageIndex.TotalTokenCount))
            .Column<double>(nameof(AICompletionUsageIndex.ResponseLatencyMs))
            .Column<DateTime>(nameof(AICompletionUsageIndex.CreatedUtc)),
            collection: options?.AICollectionName);

        await schemaBuilder.AlterIndexTableAsync<AICompletionUsageIndex>(table =>
        {
            table.CreateIndex("IDX_AICompletionUsage_DocumentId", "DocumentId",
                nameof(AICompletionUsageIndex.SessionId),
                nameof(AICompletionUsageIndex.ProfileId));

            table.CreateIndex("IDX_AICompletionUsage_UserId",
                "DocumentId",
                nameof(AICompletionUsageIndex.UserId),
                nameof(AICompletionUsageIndex.CreatedUtc));

        }, collection: options?.AICollectionName);
    }
}
