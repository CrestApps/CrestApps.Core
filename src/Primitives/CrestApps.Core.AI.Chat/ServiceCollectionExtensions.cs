using CrestApps.Core.AI.Chat.Handlers;
using CrestApps.Core.AI.Chat.Models;
using CrestApps.Core.AI.Chat.Realtime;
using CrestApps.Core.AI.Chat.Security;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.AI.Security;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Builders;
using CrestApps.Core.Services;
using CrestApps.Core.Templates.Extensions;
using Fluid;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Chat;

/// <summary>
/// Extension methods for registering chat services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default chat notification sender and built-in notification action handlers.
    /// The sender dispatches notifications to keyed <see cref="IChatNotificationTransport"/>
    /// implementations, which must be registered separately by each host application.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIChatNotifications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IChatNotificationSender, DefaultChatNotificationSender>();
        services.TryAddKeyedScoped<IChatNotificationActionHandler, CancelTransferNotificationActionHandler>(ChatNotificationActionNames.CancelTransfer);
        services.TryAddKeyedScoped<IChatNotificationActionHandler, EndSessionNotificationActionHandler>(ChatNotificationActionNames.EndSession);

        return services;
    }

    /// <summary>
    /// Configures standard hub options (timeouts, message sizes) for a chat hub.
    /// Call this for each concrete hub type that handles AI chat traffic.
    /// </summary>
    public static IServiceCollection ConfigureCrestAppsChatHubOptions<THub>(this IServiceCollection services) where THub : Hub
    {
        ArgumentNullException.ThrowIfNull(services);

        // AddHubOptions, not services.Configure<HubOptions<THub>>. SignalR reads the typed options only when
        // they were configured through this API, which sets an internal UserHasSetValues flag. Configured the
        // other way the values are assigned and then ignored: HubConnectionHandler falls back to the global
        // HubOptions, so everything below silently had no effect. Measured by resolving the handler and reading
        // its private state -- with services.Configure<HubOptions<T>> it reports _maxParallelInvokes = 1 and
        // _maximumMessageSize = 32768 no matter what is set here.
        services.AddSignalR().AddHubOptions<THub>(options =>
        {
            // Allow long-running operations (e.g., multi-step MCP tool calls)
            // without the server dropping the connection prematurely.
            options.ClientTimeoutInterval = TimeSpan.FromMinutes(10);
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);

            // Allow larger messages for audio transcription payloads.
            options.MaximumReceiveMessageSize = 10 * 1024 * 1024;

            // A realtime (speech-to-speech) session keeps its hub invocation open for the whole conversation.
            // With the SignalR default of one parallel invocation per client, every other call from the same
            // browser — trickled ICE candidates, loading a session, clearing history, settings — would queue
            // behind it until the conversation ended. Allow a small number of concurrent invocations so the
            // signaling and regular chat traffic keep flowing during a voice session.
            options.MaximumParallelInvocationsPerClient = 8;

            // Realtime audio is uploaded as a client-to-server stream of small Base64 frames. The default
            // buffer of 10 items fills while the provider session is still opening (1-3 s), which blocks the
            // connection's dispatch loop. Buffer a few seconds of frames instead.
            options.StreamBufferCapacity = 64;
        });

        return services;
    }

    /// <summary>
    /// Adds shared chat-session processing services used by both AI profile chat
    /// and chat interactions across hosts.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIChatSessionProcessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AIChatSessionProcessingOptions>();
        services.TryAddScoped<IAIChatSessionEventService, DefaultAIChatSessionEventService>();
        services.TryAddScoped<IAIChatSessionAnalyticsRecorder>(sp => sp.GetRequiredService<IAIChatSessionEventService>());
        services.TryAddScoped<IAIChatSessionConversionGoalRecorder>(sp => sp.GetRequiredService<IAIChatSessionEventService>());
        services.TryAddScoped<DataExtractionService>();
        services.TryAddScoped<PostSessionProcessingService>();
        services.TryAddScoped<AIChatSessionPostCloseProcessor>();
        services.TryAddScoped<CrestApps.Core.AI.Chat.Realtime.RealtimeChatSessionRunner>();
        services.TryAddSingleton<CrestApps.Core.AI.Chat.Realtime.WebRtcRealtimePeerRegistry>();
        services.TryAddSingleton<CrestApps.Core.AI.Chat.Realtime.RealtimeSessionRegistry>();
        services.TryAddSingleton<AIChatSessionCloseCycleService>();
        services.TryAddSingleton<AIChatSessionCloseRunner>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AIChatSessionCloseBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOrchestrationContextBuilderHandler, ExtractedDataOrchestrationHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIChatSessionHandler, DefaultAIChatSessionAnalyticsHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIChatSessionHandler, DataExtractionChatSessionHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAIChatSessionHandler, PostSessionProcessingChatSessionHandler>());

        services.Configure<TemplateOptions>(o =>
        {
            o.MemberAccessStrategy.Register<ExtractedFieldChange>();
        });

        return services;
    }

    /// <summary>
    /// Adds the default chat interaction handlers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddCoreAIChatInteractions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDataProtection();
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.TryAddSingleton(TimeProvider.System);

        // The realtime ICE servers come from here for both the browser and the server-relay peer. The
        // default reads them from configuration; AddCloudflareRealtimeTurn replaces it for a deployment
        // whose TURN service issues credentials through an API instead.
        services.TryAddSingleton<IRealtimeIceServerProvider, OptionsRealtimeIceServerProvider>();
        services.AddOptions<AIVisitorIdentityOptions>();
        services.AddOptions<AIChatEndpointRateLimitingOptions>();
        services.AddSingleton<IAIVisitorIdentityResolver, DefaultAIVisitorIdentityResolver>();
        services.AddCoreAIChatNotifications();
        services.AddCoreAIChatSessionProcessing();
        services.TryAddScoped<CompositeAIReferenceLinkResolver>();
        services.TryAddScoped<CitationReferenceCollector>();

        // Register templates embedded in this assembly.
        services.AddTemplatesFromAssembly(typeof(ServiceCollectionExtensions).Assembly);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAICompletionContextBuilderHandler, ChatInteractionCompletionContextBuilderHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, DataSourceChatInteractionSettingsHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IChatInteractionSettingsHandler, PromptTemplateChatInteractionSettingsHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<ChatInteraction>, ChatInteractionEntryHandler>());

        return services;
    }

    /// <summary>
    /// Adds chat interactions.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The configure.</param>
    public static CrestAppsAISuiteBuilder AddChatInteractions(this CrestAppsAISuiteBuilder builder, Action<CrestAppsChatInteractionsBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIChatInteractions();

        if (configure is not null)
        {
            configure(new CrestAppsChatInteractionsBuilder(builder.Services));
        }

        return builder;
    }

    /// <summary>
    /// Configures chat hub options.
    /// </summary>
    public static CrestAppsChatInteractionsBuilder ConfigureChatHubOptions<THub>(this CrestAppsChatInteractionsBuilder builder) where THub : Hub
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.ConfigureCrestAppsChatHubOptions<THub>();

        return builder;
    }

    /// <summary>
    /// Configures visitor-identity handling for chat interactions.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The visitor-identity configuration action.</param>
    public static CrestAppsChatInteractionsBuilder ConfigureVisitorIdentity(
        this CrestAppsChatInteractionsBuilder builder,
        Action<AIVisitorIdentityOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.Configure(configure);

        return builder;
    }

    /// <summary>
    /// Configures chat rate-limit partitioning for chat interactions.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">The chat rate-limiting configuration action.</param>
    public static CrestAppsChatInteractionsBuilder ConfigureChatRateLimiting(
        this CrestAppsChatInteractionsBuilder builder,
        Action<AIChatRateLimitingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.Configure(configure);

        return builder;
    }
    /// <summary>
    /// Sources the realtime TURN servers from Cloudflare Realtime, which issues short-lived credentials
    /// through an API rather than exposing a shared secret that could be signed locally.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the Cloudflare TURN token. Usually bound from configuration instead.</param>
    /// <remarks>
    /// <para>
    /// Registering this is safe before the key exists: while <see cref="CloudflareTurnOptions.TokenId"/> and
    /// <see cref="CloudflareTurnOptions.ApiToken"/> are unset the previously registered provider answers,
    /// so a deployment running its own coturn is unaffected until it opts in.
    /// </para>
    /// <para>
    /// Prefer this over pasting a generated username and password into
    /// <see cref="RealtimeTransportOptions"/>: those are issued with a fixed lifetime and stop working when
    /// it lapses, whereas the key and token used here are long-lived and every credential the browser sees
    /// is minted on demand.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCloudflareRealtimeTurn(this IServiceCollection services, Action<CloudflareTurnOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(nameof(CloudflareRealtimeIceServerProvider));
        services.TryAddSingleton(TimeProvider.System);

        // Bound from the same section the rest of the realtime transport reads, so the token can be supplied
        // as configuration rather than in code. Without this the options would silently stay empty and the
        // provider would quietly defer to the fallback, which looks exactly like nothing being configured.
        services.AddOptions<CloudflareTurnOptions>().BindConfiguration("CrestApps:AI:RealtimeTransport:Cloudflare");

        if (configure is not null)
        {
            services.Configure(configure);
        }

        // The fallback is named concretely rather than taken as whatever happened to be registered. It is
        // not an arbitrary provider -- it is the configured-servers path used while Cloudflare is not set
        // up, and whenever an outage leaves no credentials to serve. Naming it also keeps Replace below
        // from handing the decorator itself as its own fallback.
        services.TryAddSingleton<OptionsRealtimeIceServerProvider>();

        // Replace, so calling this more than once -- which hosts do, enabling several realtime surfaces --
        // leaves exactly one provider rather than a stack of decorators each wrapping the last.
        services.Replace(ServiceDescriptor.Singleton<IRealtimeIceServerProvider, CloudflareRealtimeIceServerProvider>());

        return services;
    }

}
