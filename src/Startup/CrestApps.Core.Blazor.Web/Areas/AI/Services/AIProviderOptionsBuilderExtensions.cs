using CrestApps.Core.AI.Models;
using CrestApps.Core.Builders;

namespace CrestApps.Core.Blazor.Web.Areas.AI.Services;

public static class AIProviderOptionsBuilderExtensions
{
    public static CrestAppsAISuiteBuilder ConfigureProviderOptions(this CrestAppsAISuiteBuilder builder, IConfigurationSection configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.Configure<AIProviderOptions>(configuration);

        return builder;
    }
}
