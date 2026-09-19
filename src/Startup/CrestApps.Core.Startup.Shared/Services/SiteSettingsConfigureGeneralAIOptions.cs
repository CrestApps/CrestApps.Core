using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Startup.Shared.Services;

internal sealed class SiteSettingsConfigureGeneralAIOptions : IConfigureOptions<GeneralAIOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureGeneralAIOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(GeneralAIOptions options)
    {
        SiteSettingsGeneralAIOptionsMapper.Apply(_siteSettings.Get<GeneralAISettings>(), options);
    }
}
