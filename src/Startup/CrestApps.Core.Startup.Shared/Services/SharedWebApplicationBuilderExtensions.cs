using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog.Web;

namespace CrestApps.Core.Startup.Shared.Services;

public static class SharedWebApplicationBuilderExtensions
{
    /// <summary>
    /// Applies the shared sample-host infrastructure used by the MVC and Blazor
    /// sample applications and returns the resolved <c>App_Data</c> path.
    /// </summary>
    public static string AddSharedSampleHostDefaults(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<HostOptions>(options =>
        {
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
        });

        builder.Logging.ClearProviders();
        builder.WebHost.UseNLog();

        var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appDataPath);

        builder.Configuration.AddJsonFile("App_Data/appsettings.json", optional: true, reloadOnChange: false);
        builder.Services.AddSharedSiteSettings(appDataPath);

        return appDataPath;
    }

    /// <summary>
    /// Registers the shared site-settings store and the option bridges that map
    /// admin-managed settings into framework options.
    /// </summary>
    public static IServiceCollection AddSharedSiteSettings(this IServiceCollection services, string appDataPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(appDataPath);

        services.AddSingleton(new SiteSettingsStore(appDataPath));
        services.AddSingleton<IConfigureOptions<GeneralAIOptions>, SiteSettingsConfigureGeneralAIOptions>();
        services.AddSingleton<IConfigureOptions<AIMemoryOptions>, SiteSettingsConfigureAIMemoryOptions>();
        services.AddSingleton<IConfigureOptions<InteractionDocumentOptions>, SiteSettingsConfigureInteractionDocumentOptions>();
        services.AddSingleton<IConfigureOptions<AIDataSourceOptions>, SiteSettingsConfigureAIDataSourceOptions>();
        services.AddSingleton<IConfigureOptions<ChatInteractionMemoryOptions>, SiteSettingsConfigureChatInteractionMemoryOptions>();
        services.AddSingleton<IConfigureOptions<DefaultAIDeploymentSettings>, SiteSettingsConfigureDefaultDeploymentOptions>();

        return services;
    }
}
