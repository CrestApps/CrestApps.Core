using System.Reflection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Startup.Shared.Services;

public sealed class SiteSettingsConfigureStoredOptions<TOptions> : IConfigureOptions<TOptions>
    where TOptions : class, new()
{
    private static readonly PropertyInfo[] _properties = typeof(TOptions)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
        .ToArray();

    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureStoredOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(TOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var storedOptions = _siteSettings.Get<TOptions>();

        foreach (var property in _properties)
        {
            property.SetValue(options, property.GetValue(storedOptions));
        }
    }
}
