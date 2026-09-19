using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Startup.Shared.Services;

internal sealed class SiteSettingsConfigureChatInteractionMemoryOptions : IConfigureOptions<ChatInteractionMemoryOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureChatInteractionMemoryOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(ChatInteractionMemoryOptions options)
    {
        SiteSettingsChatInteractionMemoryOptionsMapper.Apply(_siteSettings.Get<MemoryMetadata>(), options);
    }
}
