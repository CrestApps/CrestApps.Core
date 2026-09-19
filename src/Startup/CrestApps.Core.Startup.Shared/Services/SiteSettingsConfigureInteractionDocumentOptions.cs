using CrestApps.Core.AI.Documents.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Startup.Shared.Services;

internal sealed class SiteSettingsConfigureInteractionDocumentOptions : IConfigureOptions<InteractionDocumentOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureInteractionDocumentOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(InteractionDocumentOptions options)
    {
        SiteSettingsInteractionDocumentOptionsMapper.Apply(_siteSettings.Get<InteractionDocumentSettings>(), options);
    }
}
