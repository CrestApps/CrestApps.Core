using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Handlers;

internal sealed class AIProfileHandler : CatalogEntryHandlerBase<AIProfile>
{
    internal readonly IStringLocalizer S;

    public AIProfileHandler(IStringLocalizer<AIProfileHandler> stringLocalizer)
    {
        S = stringLocalizer;
    }

    public override Task InitializingAsync(InitializingContext<AIProfile> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    public override Task UpdatingAsync(UpdatingContext<AIProfile> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    private static Task PopulateAsync(AIProfile profile, JsonNode data)
    {
        var metadata = profile.GetOrCreate<AIProfileMetadata>();

        var settings = profile.GetSettings<AIProfileSettings>();

        if (!settings.LockSystemMessage)
        {
            var systemMessage = data[nameof(AIProfileMetadata.SystemMessage)]?.GetValue<string>()?.Trim();

            if (!string.IsNullOrEmpty(systemMessage))
            {
                metadata.SystemMessage = systemMessage;

                profile.Put(metadata);
            }
        }

        return Task.CompletedTask;
    }
}
