using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Startup.Shared.Services;

internal static class SiteSettingsGeneralAIOptionsMapper
{
    public static GeneralAIOptions Create(GeneralAISettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new GeneralAIOptions();
        Apply(settings, options);

        return options;
    }

    public static void Apply(GeneralAISettings settings, GeneralAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        options.EnableAIUsageTracking = settings.EnableAIUsageTracking;
        options.EnablePreemptiveMemoryRetrieval = settings.EnablePreemptiveMemoryRetrieval;
        options.OverrideMaximumIterationsPerRequest = settings.OverrideMaximumIterationsPerRequest;
        options.MaximumIterationsPerRequest = settings.MaximumIterationsPerRequest;
        options.OverrideEnableDistributedCaching = settings.OverrideEnableDistributedCaching;
        options.EnableDistributedCaching = settings.EnableDistributedCaching;
        options.OverrideEnableOpenTelemetry = settings.OverrideEnableOpenTelemetry;
        options.EnableOpenTelemetry = settings.EnableOpenTelemetry;
    }
}
