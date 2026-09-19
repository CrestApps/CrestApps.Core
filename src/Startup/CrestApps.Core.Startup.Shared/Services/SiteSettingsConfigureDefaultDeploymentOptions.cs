using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Startup.Shared.Services;

internal sealed class SiteSettingsConfigureDefaultDeploymentOptions : IConfigureOptions<DefaultAIDeploymentSettings>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureDefaultDeploymentOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(DefaultAIDeploymentSettings options)
    {
        SiteSettingsDefaultAIDeploymentOptionsMapper.Apply(_siteSettings.Get<DefaultAIDeploymentSettings>(), options);
    }
}
