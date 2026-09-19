using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Startup.Shared.Services;

internal static class SiteSettingsChatInteractionMemoryOptionsMapper
{
    public static ChatInteractionMemoryOptions Create(MemoryMetadata settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new ChatInteractionMemoryOptions();
        Apply(settings, options);

        return options;
    }

    public static void Apply(MemoryMetadata settings, ChatInteractionMemoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        options.EnableUserMemory = settings.EnableUserMemory ?? true;
    }
}
