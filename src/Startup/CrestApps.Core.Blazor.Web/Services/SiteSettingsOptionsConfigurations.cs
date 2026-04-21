using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Blazor.Web.Services;

/// <summary>
/// Populates <see cref="GeneralAIOptions"/> from the admin-managed
/// <see cref="GeneralAISettings"/> stored in <see cref="SiteSettingsStore"/>.
/// </summary>
internal sealed class SiteSettingsConfigureGeneralAIOptions : IConfigureOptions<GeneralAIOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureGeneralAIOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

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

/// <summary>
/// Populates <see cref="AIMemoryOptions"/> from the admin-managed
/// <see cref="AIMemorySettings"/> stored in <see cref="SiteSettingsStore"/>.
/// </summary>
internal sealed class SiteSettingsConfigureAIMemoryOptions : IConfigureOptions<AIMemoryOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureAIMemoryOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(AIMemoryOptions options)
    {
        var settings = _siteSettings.Get<AIMemorySettings>();
        options.IndexProfileName = settings.IndexProfileName;
        options.TopN = settings.TopN;
    }
}

/// <summary>
/// Populates <see cref="InteractionDocumentOptions"/> from the admin-managed
/// <see cref="InteractionDocumentSettings"/> stored in <see cref="SiteSettingsStore"/>.
/// </summary>
internal sealed class SiteSettingsConfigureInteractionDocumentOptions : IConfigureOptions<InteractionDocumentOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureInteractionDocumentOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(InteractionDocumentOptions options)
    {
        var settings = _siteSettings.Get<InteractionDocumentSettings>();
        options.IndexProfileName = settings.IndexProfileName;
        options.TopN = settings.TopN;
        options.RetrievalMode = settings.RetrievalMode;
    }
}

/// <summary>
/// Populates <see cref="AIDataSourceOptions"/> from the admin-managed
/// <see cref="AIDataSourceSettings"/> stored in <see cref="SiteSettingsStore"/>.
/// </summary>
internal sealed class SiteSettingsConfigureAIDataSourceOptions : IConfigureOptions<AIDataSourceOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureAIDataSourceOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(AIDataSourceOptions options)
    {
        var settings = _siteSettings.Get<AIDataSourceSettings>();
        options.DefaultStrictness = settings.DefaultStrictness;
        options.DefaultTopNDocuments = settings.DefaultTopNDocuments;
    }
}

/// <summary>
/// Populates <see cref="ChatInteractionMemoryOptions"/> from the admin-managed
/// <see cref="MemoryMetadata"/> stored in <see cref="SiteSettingsStore"/>.
/// </summary>
internal sealed class SiteSettingsConfigureChatInteractionMemoryOptions : IConfigureOptions<ChatInteractionMemoryOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureChatInteractionMemoryOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(ChatInteractionMemoryOptions options)
    {
        var settings = _siteSettings.Get<MemoryMetadata>();
        options.EnableUserMemory = settings.EnableUserMemory ?? true;
    }
}

/// <summary>
/// Populates <see cref="DefaultAIDeploymentSettings"/> from the admin-managed
/// settings stored in <see cref="SiteSettingsStore"/> so that framework services
/// (e.g. <c>AIChatHubCore</c>) can resolve default deployment names via IOptions.
/// </summary>
internal sealed class SiteSettingsConfigureDefaultDeploymentOptions : IConfigureOptions<DefaultAIDeploymentSettings>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureDefaultDeploymentOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(DefaultAIDeploymentSettings options)
    {
        var settings = _siteSettings.Get<DefaultAIDeploymentSettings>();
        options.DefaultChatDeploymentName = settings.DefaultChatDeploymentName;
        options.DefaultUtilityDeploymentName = settings.DefaultUtilityDeploymentName;
        options.DefaultEmbeddingDeploymentName = settings.DefaultEmbeddingDeploymentName;
        options.DefaultImageDeploymentName = settings.DefaultImageDeploymentName;
        options.DefaultSpeechToTextDeploymentName = settings.DefaultSpeechToTextDeploymentName;
        options.DefaultTextToSpeechDeploymentName = settings.DefaultTextToSpeechDeploymentName;
        options.DefaultTextToSpeechVoiceId = settings.DefaultTextToSpeechVoiceId;
    }
}
