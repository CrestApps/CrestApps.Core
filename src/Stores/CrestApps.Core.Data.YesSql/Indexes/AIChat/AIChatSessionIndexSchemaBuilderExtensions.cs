using YesSql.Sql;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

public static class AIChatSessionIndexSchemaBuilderExtensions
{
    public static Task CreateAIChatSessionIndexSchemaAsync(this ISchemaBuilder schemaBuilder)
    {
        return schemaBuilder.CreateMapIndexTableAsync<AIChatSessionIndex>(table => table.Column<string>(nameof(AIChatSessionIndex.ItemId), column => column.WithLength(44)).Column<string>(nameof(AIChatSessionIndex.SessionId), column => column.WithLength(44)).Column<string>(nameof(AIChatSessionIndex.ProfileId), column => column.WithLength(26)).Column<string>(nameof(AIChatSessionIndex.UserId), column => column.WithLength(255)).Column<int>(nameof(AIChatSessionIndex.Status)).Column<DateTime>(nameof(AIChatSessionIndex.LastActivityUtc)));
    }
}