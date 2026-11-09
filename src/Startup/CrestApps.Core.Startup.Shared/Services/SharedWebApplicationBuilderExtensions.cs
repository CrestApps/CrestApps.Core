using CrestApps.Core.AI.Chat.Models;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Security;
using CrestApps.Core.OpenTelemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog.Web;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace CrestApps.Core.Startup.Shared.Services;

public static class SharedWebApplicationBuilderExtensions
{
    private const string AppDataPathConfigurationKey = "CrestApps:AppDataPath";

    private const string OtlpEndpointConfigurationKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// The folder under <c>App_Data</c> that the sample hosts allow the file-system connector to read.
    /// </summary>
    private const string FileSourcesFolderName = "file-sources";

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

        // Allow the App_Data path to be overridden via configuration (e.g. an environment
        // variable CrestApps__AppDataPath). When running under Aspire + Visual Studio the
        // content root points to the project source directory, and any file writes there
        // can trigger VS to stop the debug session. Redirecting App_Data outside the
        // source tree avoids that problem.
        var configuredAppDataPath = builder.Configuration[AppDataPathConfigurationKey];

        var appDataPath = !string.IsNullOrWhiteSpace(configuredAppDataPath)
            ? configuredAppDataPath
            : Path.Combine(builder.Environment.ContentRootPath, "App_Data");
        var projectAppDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");

        Directory.CreateDirectory(appDataPath);

        // The sandbox the file-system connector is pointed at in appsettings.json. It is created under the
        // content root's App_Data rather than a redirected one, because the configured allowed root is a
        // content-root-relative path and that is what the connector resolves. A host that moves App_Data
        // moves this entry too.
        Directory.CreateDirectory(Path.Combine(projectAppDataPath, FileSourcesFolderName));

        var keysDirectory = new DirectoryInfo(Path.Combine(appDataPath, "DataProtection-Keys"));
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(keysDirectory);

        AddSharedAppDataConfigurationSources(builder.Configuration, projectAppDataPath, appDataPath);

        builder.Services.AddSharedSiteSettings(appDataPath);

        return appDataPath;
    }

    /// <summary>
    /// Sends the sample host's traces, metrics and logs to an OpenTelemetry collector, such as the Aspire dashboard,
    /// when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set. The Aspire AppHost sets it for every project it runs; without
    /// it nothing is collected or exported. Call it after <see cref="AddSharedSampleHostDefaults"/>, which clears the
    /// logging providers.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The builder.</returns>
    public static WebApplicationBuilder AddSharedSampleOpenTelemetry(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointConfigurationKey]))
        {
            return builder;
        }

        // Logs reach both NLog and the collector, with their scopes and trace ids.
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("System.Runtime"))
            .AddCrestAppsOpenTelemetry()
            .UseOtlpExporter();

        return builder;
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IOptionsChangeTokenSource<>), typeof(SiteSettingsOptionsChangeTokenSource<>)));
        services.AddSingleton<IConfigureOptions<GeneralAIOptions>, SiteSettingsConfigureGeneralAIOptions>();
        services.AddSingleton<IConfigureOptions<AIMemoryOptions>, SiteSettingsConfigureAIMemoryOptions>();
        services.AddSingleton<IConfigureOptions<AIChatSessionProcessingOptions>, SiteSettingsConfigureStoredOptions<AIChatSessionProcessingOptions>>();
        services.AddSingleton<IConfigureOptions<InteractionDocumentOptions>, SiteSettingsConfigureInteractionDocumentOptions>();
        services.AddSingleton<IConfigureOptions<AIDataSourceOptions>, SiteSettingsConfigureAIDataSourceOptions>();
        services.AddSingleton<IConfigureOptions<ChatInteractionMemoryOptions>, SiteSettingsConfigureChatInteractionMemoryOptions>();
        services.AddSingleton<IConfigureOptions<DefaultAIDeploymentSettings>, SiteSettingsConfigureDefaultDeploymentOptions>();
        services.AddSingleton<IConfigureOptions<AIVisitorIdentityOptions>, SiteSettingsConfigureStoredOptions<AIVisitorIdentityOptions>>();
        services.AddSingleton<IConfigureOptions<PromptSecurityOptions>, SiteSettingsConfigureStoredOptions<PromptSecurityOptions>>();

        return services;
    }

    private static void AddSharedAppDataConfigurationSources(
        ConfigurationManager configuration,
        string projectAppDataPath,
        string appDataPath)
    {
        configuration.AddJsonFile(
            Path.Combine(projectAppDataPath, "appsettings.json"),
            optional: true,
            reloadOnChange: false);

        if (PathsMatch(projectAppDataPath, appDataPath))
        {
            return;
        }

        configuration.AddJsonFile(
            Path.Combine(appDataPath, "appsettings.json"),
            optional: true,
            reloadOnChange: false);
    }

    private static bool PathsMatch(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
