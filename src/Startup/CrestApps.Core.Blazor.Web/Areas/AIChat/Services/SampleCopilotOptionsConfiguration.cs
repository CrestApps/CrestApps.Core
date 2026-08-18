using CrestApps.Core.AI.Copilot.Models;
using CrestApps.Core.Startup.Shared.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Services;

internal sealed class SampleCopilotOptionsConfiguration : IConfigureOptions<CopilotOptions>
{
    private const string ProtectorPurpose = "CrestApps.Core.Blazor.Web.CopilotSettings";

    private static readonly Action<ILogger, Exception> _failedToUnprotectClientSecret =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1001, nameof(FailedToUnprotectClientSecret)),
            "Failed to unprotect Copilot client secret.");

    private static readonly Action<ILogger, Exception> _failedToUnprotectApiKey =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1002, nameof(FailedToUnprotectApiKey)),
            "Failed to unprotect Copilot API key.");

    private readonly SiteSettingsStore _siteSettings;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<SampleCopilotOptionsConfiguration> _logger;

    public SampleCopilotOptionsConfiguration(
        SiteSettingsStore siteSettings,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SampleCopilotOptionsConfiguration> logger)
    {
        _siteSettings = siteSettings;
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
    }

    public void Configure(CopilotOptions options)
    {
        Apply(_siteSettings.Get<CopilotSettings>(), options, _dataProtectionProvider, _logger);
    }

    public static CopilotOptions Create(
        CopilotSettings settings,
        IDataProtectionProvider dataProtectionProvider,
        ILogger logger)
    {
        var options = new CopilotOptions();
        Apply(settings, options, dataProtectionProvider, logger);

        return options;
    }

    public static void Apply(
        CopilotSettings settings,
        CopilotOptions options,
        IDataProtectionProvider dataProtectionProvider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentNullException.ThrowIfNull(logger);

        options.AuthenticationType = settings.AuthenticationType;
        options.ClientId = settings.ClientId;
        options.Scopes = settings.Scopes ?? ["user:email", "read:org"];
        options.ProviderType = settings.ProviderType;
        options.BaseUrl = settings.BaseUrl;
        options.WireApi = settings.WireApi ?? "completions";
        options.DefaultModel = settings.DefaultModel;
        options.AzureApiVersion = settings.AzureApiVersion;

        var protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

        if (!string.IsNullOrWhiteSpace(settings.ProtectedClientSecret))
        {
            try
            {
                options.ClientSecret = protector.Unprotect(settings.ProtectedClientSecret);
            }
            catch (Exception ex)
            {
                FailedToUnprotectClientSecret(logger, ex);
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
                FailedToUnprotectApiKey(logger, ex);
            }
        }
    }

    private static void FailedToUnprotectClientSecret(ILogger logger, Exception exception)
    {
        _failedToUnprotectClientSecret(logger, exception);
    }

    private static void FailedToUnprotectApiKey(ILogger logger, Exception exception)
    {
        _failedToUnprotectApiKey(logger, exception);
    }
}
