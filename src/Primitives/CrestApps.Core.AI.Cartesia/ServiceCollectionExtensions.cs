using CrestApps.Core.AI.Cartesia.Services;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Cartesia;

/// <summary>
/// Provides extension methods for registering the Cartesia provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Cartesia speech provider: text-to-speech, speech-to-text, voice search and voice cloning.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAICartesia(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(CartesiaConstants.HttpClientName);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIClientProvider, CartesiaClientProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IVoiceLibraryProvider, CartesiaClientProvider>());

        services.AddCoreAIDeploymentProvider(CartesiaConstants.ClientName, o =>
        {
            o.DisplayName = new LocalizedString("Cartesia", "Cartesia");
            o.Description = new LocalizedString("Cartesia", "Use Cartesia Sonic voices and Ink speech recognition for speech deployments.");
            o.UseContainedConnection = true;
        });

        return services;
    }

    /// <summary>
    /// Adds the Cartesia speech provider.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsAISuiteBuilder AddCartesia(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAICartesia();

        return builder;
    }
}
