using CrestApps.Core.AI.Animations.Handlers;
using CrestApps.Core.AI.Animations.Services;
using CrestApps.Core.AI.Animations.Storage;
using CrestApps.Core.AI.Animations.Tools;
using CrestApps.Core.AI.Animations.Video;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Builders;
using CrestApps.Core.Templates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CrestApps.Core.AI.Animations;

/// <summary>
/// Provides extension methods for registering the animation agent.
/// </summary>
public static class AnimationServiceCollectionExtensions
{
    /// <summary>
    /// Adds the system animation agent: its tools, the script sandbox, renderer and encoder behind them, the store
    /// its videos are kept in, and the handlers that delete them with the conversation or its history.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// The host must also map the endpoint the chat plays the videos from, with
    /// <see cref="Endpoints.AnimationAssetEndpoint.AddAnimationAssetEndpoint"/>, and make FFmpeg available. Without
    /// FFmpeg the agent is simply not offered.
    /// </remarks>
    public static IServiceCollection AddCoreAIAnimations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AnimationOptions>()
            .BindConfiguration(AnimationOptions.ConfigurationSectionName)
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                options.BasePath = string.IsNullOrWhiteSpace(options.BasePath)
                    ? Path.Combine(environment.ContentRootPath, "App_Data", "Animations")
                    : Path.Combine(environment.ContentRootPath, options.BasePath);

                if (string.IsNullOrWhiteSpace(options.TempDirectory))
                {
                    options.TempDirectory = Path.Combine(Path.GetTempPath(), "crestapps-animations");
                }
            });

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<FFmpegProbe>();
        services.TryAddSingleton<FFmpegVideoEncoder>();
        services.TryAddSingleton<AnimationRenderService>();
        services.TryAddScoped<AnimationFrameReviewer>();
        services.TryAddSingleton<IAnimationStore, FileSystemAnimationStore>();

        services.AddTemplatesFromAssembly(typeof(AnimationServiceCollectionExtensions).Assembly);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIProfileProvider, AnimationAgentProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConversationWorkspaceCleanupHandler, AnimationWorkspaceCleanupHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionHistoryHandler, AnimationHistoryClearedHandler>());

        services.AddCoreAITool<RenderAnimationTool>(RenderAnimationTool.TheName)
            .WithTitle("Render Animation")
            .WithDescription("Runs JavaScript the agent writes in a sandbox to draw a scene, and plays it in the chat as an MP4.")
            .WithCategory("Animations")
            .Hidden();

        services.AddCoreAITool<GetAnimationSourceTool>(GetAnimationSourceTool.TheName)
            .WithTitle("Get Animation Source")
            .WithDescription("Returns the script the conversation's latest video was made with, so it can be edited.")
            .WithCategory("Animations")
            .Hidden();

        return services;
    }

    /// <summary>
    /// Adds the system animation agent to document processing.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static CrestAppsDocumentProcessingBuilder AddAnimations(this CrestAppsDocumentProcessingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIAnimations();

        return builder;
    }
}
