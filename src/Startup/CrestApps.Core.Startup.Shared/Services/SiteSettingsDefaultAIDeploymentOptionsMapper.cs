using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Startup.Shared.Services;

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
        options.DefaultVisionDeploymentName = settings.DefaultVisionDeploymentName;
        options.DefaultSpeechToTextDeploymentName = settings.DefaultSpeechToTextDeploymentName;
        options.DefaultTextToSpeechDeploymentName = settings.DefaultTextToSpeechDeploymentName;
        options.DefaultTextToSpeechVoiceId = settings.DefaultTextToSpeechVoiceId;
        options.DefaultRealtimeDeploymentName = settings.DefaultRealtimeDeploymentName;
        options.DefaultRealtimeVoiceId = settings.DefaultRealtimeVoiceId;
    }
}
