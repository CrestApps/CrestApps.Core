using CrestApps.Core.AI.Copilot.Models;
using CrestApps.Core.Startup.Shared.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Services;

internal sealed class MvcCopilotOptionsConfiguration : IConfigureOptions<CopilotOptions>
{
    private const string ProtectorPurpose = "CrestApps.Core.Blazor.Web.CopilotSettings";

    private readonly SiteSettingsStore _siteSettings;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<MvcCopilotOptionsConfiguration> _logger;

    public MvcCopilotOptionsConfiguration(
        SiteSettingsStore siteSettings,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<MvcCopilotOptionsConfiguration> logger)
    {
        _siteSettings = siteSettings;
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    public void Configure(CopilotOptions options)
    {
        var settings = _siteSettings.Get<CopilotSettings>();

        if (settings == null)
        {
            return;
        }

        options.AuthenticationType = settings.AuthenticationType;
        options.ClientId = settings.ClientId;
        options.Scopes = settings.Scopes ?? ["user:email", "read:org"];
        options.ProviderType = settings.ProviderType;
        options.BaseUrl = settings.BaseUrl;
        options.WireApi = settings.WireApi ?? "completions";
        options.DefaultModel = settings.DefaultModel;
        options.AzureApiVersion = settings.AzureApiVersion;

        var protector = _dataProtectionProvider.CreateProtector(ProtectorPurpose);

        if (!string.IsNullOrWhiteSpace(settings.ProtectedClientSecret))
        {
            try
            {
                options.ClientSecret = protector.Unprotect(settings.ProtectedClientSecret);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unprotect Copilot client secret.");
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.ProtectedApiKey))
        {
            try
            {
                options.ApiKey = protector.Unprotect(settings.ProtectedApiKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unprotect Copilot API key.");
            }
        }
    }
}
