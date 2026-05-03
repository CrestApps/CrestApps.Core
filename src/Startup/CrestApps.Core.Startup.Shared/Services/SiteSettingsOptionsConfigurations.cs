using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Startup.Shared.Services;

internal sealed class SiteSettingsConfigureGeneralAIOptions : IConfigureOptions<GeneralAIOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureGeneralAIOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(GeneralAIOptions options)
    {
        SiteSettingsGeneralAIOptionsMapper.Apply(_siteSettings.Get<GeneralAISettings>(), options);
    }
}

internal static class SiteSettingsGeneralAIOptionsMapper
{
    public static GeneralAIOptions Create(GeneralAISettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new GeneralAIOptions();
        Apply(settings, options);

        return options;
    }

    public static void Apply(GeneralAISettings settings, GeneralAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

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

    public SiteSettingsConfigureAIMemoryOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(AIMemoryOptions options)
    {
        SiteSettingsAIMemoryOptionsMapper.Apply(_siteSettings.Get<AIMemoryOptions>(), options);
    }
}

internal static class SiteSettingsAIMemoryOptionsMapper
{
    public static AIMemoryOptions Create(AIMemoryOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new AIMemoryOptions();
        Apply(settings, options);

        return options;
    }

    public static void Apply(AIMemoryOptions settings, AIMemoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        options.IndexProfileName = settings.IndexProfileName;
        options.TopN = settings.TopN;
    }
}

internal sealed class SiteSettingsConfigureInteractionDocumentOptions : IConfigureOptions<InteractionDocumentOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureInteractionDocumentOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(InteractionDocumentOptions options)
    {
        SiteSettingsInteractionDocumentOptionsMapper.Apply(_siteSettings.Get<InteractionDocumentSettings>(), options);
    }
}

internal static class SiteSettingsInteractionDocumentOptionsMapper
{
    public static InteractionDocumentOptions Create(InteractionDocumentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new InteractionDocumentOptions();
        Apply(settings, options);

        return options;
    }

    public static void Apply(InteractionDocumentSettings settings, InteractionDocumentOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        options.IndexProfileName = settings.IndexProfileName;
        options.TopN = settings.TopN;
        options.RetrievalMode = settings.RetrievalMode;
    }
}

internal sealed class SiteSettingsConfigureAIDataSourceOptions : IConfigureOptions<AIDataSourceOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureAIDataSourceOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(AIDataSourceOptions options)
    {
        SiteSettingsAIDataSourceOptionsMapper.Apply(_siteSettings.Get<AIDataSourceSettings>(), options);
    }
}

internal static class SiteSettingsAIDataSourceOptionsMapper
{
    public static AIDataSourceOptions Create(AIDataSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new AIDataSourceOptions();
        Apply(settings, options);

        return options;
    }

    public static void Apply(AIDataSourceSettings settings, AIDataSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        options.DefaultStrictness = settings.DefaultStrictness;
        options.DefaultTopNDocuments = settings.DefaultTopNDocuments;
    }
}

internal sealed class SiteSettingsConfigureChatInteractionMemoryOptions : IConfigureOptions<ChatInteractionMemoryOptions>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureChatInteractionMemoryOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(ChatInteractionMemoryOptions options)
    {
        SiteSettingsChatInteractionMemoryOptionsMapper.Apply(_siteSettings.Get<MemoryMetadata>(), options);
    }
}

internal static class SiteSettingsChatInteractionMemoryOptionsMapper
{
    public static ChatInteractionMemoryOptions Create(MemoryMetadata settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new ChatInteractionMemoryOptions();
        Apply(settings, options);

        return options;
    }

    public static void Apply(MemoryMetadata settings, ChatInteractionMemoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        options.EnableUserMemory = settings.EnableUserMemory ?? true;
    }
}

internal sealed class SiteSettingsConfigureDefaultDeploymentOptions : IConfigureOptions<DefaultAIDeploymentSettings>
{
    private readonly SiteSettingsStore _siteSettings;

    public SiteSettingsConfigureDefaultDeploymentOptions(SiteSettingsStore siteSettings)
    {
        _siteSettings = siteSettings;
    }

    public void Configure(DefaultAIDeploymentSettings options)
    {
        SiteSettingsDefaultAIDeploymentOptionsMapper.Apply(_siteSettings.Get<DefaultAIDeploymentSettings>(), options);
    }
}

internal static class SiteSettingsDefaultAIDeploymentOptionsMapper
{
    public static DefaultAIDeploymentSettings Create(DefaultAIDeploymentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new DefaultAIDeploymentSettings();
        Apply(settings, options);

        return options;
    }

    public static void Apply(DefaultAIDeploymentSettings settings, DefaultAIDeploymentSettings options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        options.DefaultChatDeploymentName = settings.DefaultChatDeploymentName;
        options.DefaultUtilityDeploymentName = settings.DefaultUtilityDeploymentName;
        options.DefaultEmbeddingDeploymentName = settings.DefaultEmbeddingDeploymentName;
        options.DefaultImageDeploymentName = settings.DefaultImageDeploymentName;
        options.DefaultSpeechToTextDeploymentName = settings.DefaultSpeechToTextDeploymentName;
        options.DefaultTextToSpeechDeploymentName = settings.DefaultTextToSpeechDeploymentName;
        options.DefaultTextToSpeechVoiceId = settings.DefaultTextToSpeechVoiceId;
    }
}
