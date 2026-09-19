using CrestApps.Core.AI.Memory;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Startup.Shared.Services;

internal sealed class SiteSettingsConfigureAIMemoryOptions : IConfigureOptions<AIMemoryOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureAIMemoryOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(AIMemoryOptions options)
    {
        SiteSettingsAIMemoryOptionsMapper.Apply(_siteSettings.Get<AIMemoryOptions>(), options);
    }
}
