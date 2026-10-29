using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.ElevenLabs.Services;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.ElevenLabs;

/// <summary>
/// Provides extension methods for registering the ElevenLabs provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ElevenLabs speech provider: text-to-speech, speech-to-text, voice search and voice cloning.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIElevenLabs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(ElevenLabsConstants.HttpClientName);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIClientProvider, ElevenLabsClientProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IVoiceLibraryProvider, ElevenLabsClientProvider>());

        services.AddCoreAIDeploymentProvider(ElevenLabsConstants.ClientName, o =>
        {
            o.DisplayName = new LocalizedString("ElevenLabs", "ElevenLabs");
            o.Description = new LocalizedString("ElevenLabs", "Use ElevenLabs voices and Scribe speech recognition for speech deployments.");
            o.UseContainedConnection = true;
        });

        return services;
    }

    /// <summary>
    /// Adds the ElevenLabs speech provider.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsAISuiteBuilder AddElevenLabs(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIElevenLabs();

        return builder;
    }
}
