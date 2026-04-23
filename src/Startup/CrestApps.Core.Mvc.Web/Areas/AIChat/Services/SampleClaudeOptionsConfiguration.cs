using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.Startup.Shared.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Mvc.Web.Areas.AIChat.Services;

internal sealed class SampleClaudeOptionsConfiguration : IConfigureOptions<ClaudeOptions>
{
    private const string ProtectorPurpose = "CrestApps.Core.Mvc.Web.ClaudeSettings";

    private readonly SiteSettingsStore _siteSettings;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<SampleClaudeOptionsConfiguration> _logger;

    public SampleClaudeOptionsConfiguration(
        SiteSettingsStore siteSettings,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SampleClaudeOptionsConfiguration> logger)
    {
        _siteSettings = siteSettings;
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    public void Configure(ClaudeOptions options)
    {
        var settings = _siteSettings.Get<ClaudeSettings>();
        if (settings == null)
        {
            return;
        }

        options.BaseUrl = settings.BaseUrl;
        options.DefaultModel = settings.DefaultModel;

        if (settings.AuthenticationType != ClaudeAuthenticationType.ApiKey)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.ProtectedApiKey))
        {
            return;
        }

        try
        {
            var protector = _dataProtectionProvider.CreateProtector(ProtectorPurpose);
            options.ApiKey = protector.Unprotect(settings.ProtectedApiKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unprotect Anthropic API key.");
        }
    }
}
