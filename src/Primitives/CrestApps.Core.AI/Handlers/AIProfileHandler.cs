using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Handlers;

internal sealed class AIProfileHandler : CatalogEntryHandlerBase<AIProfile>
{
    internal readonly IStringLocalizer S;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIProfileHandler"/> class.
    /// </summary>
    /// <param name="stringLocalizer">The string localizer.</param>
    public AIProfileHandler(IStringLocalizer<AIProfileHandler> stringLocalizer)
    {
        S = stringLocalizer;
    }

    /// <summary>
    /// Initializings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override Task InitializingAsync(InitializingContext<AIProfile> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    /// <summary>
    /// Updatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
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
