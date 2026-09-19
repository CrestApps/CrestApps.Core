using CrestApps.Core.AI.Memory;

namespace CrestApps.Core.Startup.Shared.Services;

internal static class SiteSettingsAIMemoryOptionsMapper
{
    public static AIMemoryOptions Create(AIMemoryOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new AIMemoryOptions();
        Apply(settings, options);

        return options;
    }

    public static void Apply(AIMemoryOptions settings, AIMemoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        options.IndexProfileName = settings.IndexProfileName;
        options.TopN = settings.TopN;
    }
}
