using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Startup.Shared.Services;

internal static class SiteSettingsAIDataSourceOptionsMapper
{
    public static AIDataSourceOptions Create(AIDataSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new AIDataSourceOptions();
        Apply(settings, options);

        return options;
    }

    public static void Apply(AIDataSourceSettings settings, AIDataSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        options.DefaultStrictness = settings.DefaultStrictness;
        options.DefaultTopNDocuments = settings.DefaultTopNDocuments;
    }
}
