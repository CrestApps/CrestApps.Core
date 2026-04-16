using CrestApps.Core.AI.Claude.Models;
using CrestApps.Core.AI.Claude.Services;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Models;
using CrestApps.Core.Mvc.Web.Areas.AIChat.Services;
using CrestApps.Core.Mvc.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Framework.Chat.Claude;

public sealed class MvcClaudeOptionsConfigurationTests
{
    [Fact]
    public void ScopedClaudeServices_ShouldSeeUpdatedSettingsInNewScope()
    {
        var appDataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var settingsStore = new SiteSettingsStore(appDataPath);
            var dataProtectionProvider = new PassthroughDataProtectionProvider();
            var protectedApiKey = dataProtectionProvider
                .CreateProtector("CrestApps.Core.Mvc.Web.ClaudeSettings")
                .Protect("test-api-key");

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(settingsStore);
            services.AddSingleton<IDataProtectionProvider>(dataProtectionProvider);
            services.AddOptions();
            services.AddSingleton<IConfigureOptions<ClaudeOptions>>(sp => new SiteSettingsClaudeOptionsSetup(
                sp.GetRequiredService<SiteSettingsStore>(),
                sp.GetRequiredService<IDataProtectionProvider>(),
                sp.GetRequiredService<ILogger<SiteSettingsClaudeOptionsSetup>>()));
            services.AddScoped<ClaudeClientService>();

            using var serviceProvider = services.BuildServiceProvider();

            using (var scope = serviceProvider.CreateScope())
            {
                var options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<ClaudeOptions>>().Value;
                var clientService = scope.ServiceProvider.GetRequiredService<ClaudeClientService>();

                Assert.False(options.IsConfigured());
                Assert.Throws<InvalidOperationException>(() => clientService.CreateClient());
            }

            settingsStore.Set(new ClaudeSettings
            {
                AuthenticationType = ClaudeAuthenticationType.ApiKey,
                ProtectedApiKey = protectedApiKey,
                DefaultModel = "claude-sonnet-4-6",
            });

            using (var scope = serviceProvider.CreateScope())
            {
                var options = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<ClaudeOptions>>().Value;
                var clientService = scope.ServiceProvider.GetRequiredService<ClaudeClientService>();

                Assert.True(options.IsConfigured());
                Assert.Equal("test-api-key", options.ApiKey);
                Assert.Equal("claude-sonnet-4-6", options.DefaultModel);
                Assert.NotNull(clientService.CreateClient());
            }
        }
        finally
        {
            if (Directory.Exists(appDataPath))
            {
                Directory.Delete(appDataPath, recursive: true);
            }
        }
    }

    private sealed class PassthroughDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new PassthroughDataProtector();
    }

    private sealed class PassthroughDataProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;

        public byte[] Protect(byte[] plaintext) => plaintext;

        public byte[] Unprotect(byte[] protectedData) => protectedData;
    }

    private sealed class SiteSettingsClaudeOptionsSetup : IConfigureOptions<ClaudeOptions>
    {
        private const string ProtectorPurpose = "CrestApps.Core.Mvc.Web.ClaudeSettings";

        private readonly SiteSettingsStore _siteSettings;
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private readonly ILogger<SiteSettingsClaudeOptionsSetup> _logger;

        public SiteSettingsClaudeOptionsSetup(
            SiteSettingsStore siteSettings,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<SiteSettingsClaudeOptionsSetup> logger)
        {
            _siteSettings = siteSettings;
            _dataProtectionProvider = dataProtectionProvider;
            _logger = logger;
        }

        public void Configure(ClaudeOptions options)
        {
            var settings = _siteSettings.Get<ClaudeSettings>();
            options.BaseUrl = settings.BaseUrl;
            options.DefaultModel = settings.DefaultModel;

            if (settings.AuthenticationType != ClaudeAuthenticationType.ApiKey || string.IsNullOrWhiteSpace(settings.ProtectedApiKey))
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
}
