using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Startup.Shared.Services;

internal sealed class SiteSettingsConfigureAIDataSourceOptions : IConfigureOptions<AIDataSourceOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureAIDataSourceOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(AIDataSourceOptions options)
    {
        SiteSettingsAIDataSourceOptionsMapper.Apply(_siteSettings.Get<AIDataSourceSettings>(), options);
    }
}
