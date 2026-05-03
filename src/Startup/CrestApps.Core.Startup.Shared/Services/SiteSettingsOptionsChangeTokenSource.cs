using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace CrestApps.Core.Startup.Shared.Services;

internal sealed class SiteSettingsOptionsChangeTokenSource<TOptions> : IOptionsChangeTokenSource<TOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsOptionsChangeTokenSource(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public string Name => Options.DefaultName;

    public IChangeToken GetChangeToken()
    {
        return _siteSettings.GetChangeToken();
    }
}
