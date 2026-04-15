using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.Areas.Admin.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Blazor.Web.Services;

internal sealed class SiteSettingsConfigureGeneralAIOptions : IConfigureOptions<GeneralAIOptions>
{
    private readonly SiteSettingsStore _siteSettings;
    public SiteSettingsConfigureGeneralAIOptions(SiteSettingsStore siteSettings) => _siteSettings = siteSettings;
    public void Configure(GeneralAIOptions options)
    {
        var settings = _siteSettings.Get<GeneralAISettings>();
        options.EnableAIUsageTracking = settings.EnableAIUsageTracking;
        options.EnablePreemptiveMemoryRetrieval = settings.EnablePreemptiveMemoryRetrieval;
        options.OverrideMaximumIterationsPerRequest = settings.OverrideMaximumIterationsPerRequest;
        options.MaximumIterationsPerRequest = settings.MaximumIterationsPerRequest;
        options.OverrideEnableDistributedCaching = settings.OverrideEnableDistributedCaching;
        options.EnableDistributedCaching = settings.EnableDistributedCaching;
        options.OverrideEnableOpenTelemetry = settings.OverrideEnableOpenTelemetry;
        options.EnableOpenTelemetry = settings.EnableOpenTelemetry;
    }
}

internal sealed class SiteSettingsConfigureAIMemoryOptions : IConfigureOptions<AIMemoryOptions>
{
    private readonly SiteSettingsStore _siteSettings;
    public SiteSettingsConfigureAIMemoryOptions(SiteSettingsStore siteSettings) => _siteSettings = siteSettings;
    public void Configure(AIMemoryOptions options)
    {
        var settings = _siteSettings.Get<AIMemorySettings>();
        options.IndexProfileName = settings.IndexProfileName;
        options.TopN = settings.TopN;
    }
}

internal sealed class SiteSettingsConfigureInteractionDocumentOptions : IConfigureOptions<InteractionDocumentOptions>
{
    private readonly SiteSettingsStore _siteSettings;
    public SiteSettingsConfigureInteractionDocumentOptions(SiteSettingsStore siteSettings) => _siteSettings = siteSettings;
    public void Configure(InteractionDocumentOptions options)
    {
        var settings = _siteSettings.Get<InteractionDocumentSettings>();
        options.IndexProfileName = settings.IndexProfileName;
        options.TopN = settings.TopN;
    }
}

internal sealed class SiteSettingsConfigureAIDataSourceOptions : IConfigureOptions<AIDataSourceOptions>
{
    private readonly SiteSettingsStore _siteSettings;
    public SiteSettingsConfigureAIDataSourceOptions(SiteSettingsStore siteSettings) => _siteSettings = siteSettings;
    public void Configure(AIDataSourceOptions options)
    {
        var settings = _siteSettings.Get<AIDataSourceSettings>();
        options.DefaultStrictness = settings.DefaultStrictness;
        options.DefaultTopNDocuments = settings.DefaultTopNDocuments;
    }
}

internal sealed class SiteSettingsConfigureChatInteractionMemoryOptions : IConfigureOptions<ChatInteractionMemoryOptions>
{
    private readonly SiteSettingsStore _siteSettings;
    public SiteSettingsConfigureChatInteractionMemoryOptions(SiteSettingsStore siteSettings) => _siteSettings = siteSettings;
    public void Configure(ChatInteractionMemoryOptions options)
    {
        var settings = _siteSettings.Get<MemoryMetadata>();
        options.EnableUserMemory = settings.EnableUserMemory ?? true;
    }
}
