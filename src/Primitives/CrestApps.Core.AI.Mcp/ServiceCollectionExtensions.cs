using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Mcp.Functions;
using CrestApps.Core.AI.Mcp.Handlers;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Builders;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Mcp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreAIMcpServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMemoryCache();
        services.AddDistributedMemoryCache();
        services.AddHttpClient(McpConstants.HttpClientName)
            .AddStandardResilienceHandler();

        services.AddDataProtection();

        services.TryAddScoped<McpService>();
        services.TryAddScoped<CrestApps.Core.AI.Services.IOAuth2TokenService, CrestApps.Core.AI.Services.DefaultOAuth2TokenService>();
        services.TryAddSingleton<IMcpMetadataPromptGenerator, DefaultMcpMetadataPromptGenerator>();
        services.TryAddSingleton<IMcpCapabilityEmbeddingCacheProvider, InMemoryMcpCapabilityEmbeddingCacheProvider>();
        services.TryAddScoped<IMcpServerMetadataCacheProvider, DefaultMcpServerMetadataProvider>();
        services.TryAddScoped<IMcpCapabilityResolver, DefaultMcpCapabilityResolver>();

        services.AddOptions<McpCapabilityResolverOptions>();
        services.AddOptions<McpMetadataCacheOptions>();

        services.AddCoreAISseMcpClientTransport();

        return services;
    }

    public static IServiceCollection AddCoreAISseMcpClientTransport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMcpClientTransportProvider, SseClientTransportProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ICatalogEntryHandler<McpConnection>, SseMcpConnectionSettingsHandler>());

        services.Configure<McpClientAIOptions>(options =>
        {
            options.AddTransportType(McpConstants.TransportTypes.Sse, entry =>
            {
                entry.DisplayName = new LocalizedString("Server-Sent Events", "Server-Sent Events");
                entry.Description = new LocalizedString(
                    "Server-Sent Events Description",
                    "Uses Server-Sent Events over HTTP to receive streaming responses from a remote model server. Great for real-time output from hosted models.");
            });
        });

        return services;
    }

    public static IServiceCollection AddCoreAIStdIoMcpClientTransport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMcpClientTransportProvider, StdioClientTransportProvider>());

        services.Configure<McpClientAIOptions>(options =>
        {
            options.AddTransportType(McpConstants.TransportTypes.StdIo, entry =>
            {
                entry.DisplayName = new LocalizedString("Standard Input/Output", "Standard Input/Output");
                entry.Description = new LocalizedString(
                    "Standard Input/Output Description",
                    "Uses standard input/output streams to communicate with a locally running model process. Ideal for local subprocess integration.");
            });
        });

        return services;
    }

    /// <summary>
    /// Adds MCP client services including transport providers, OAuth2, and the core
    /// <see cref="McpService"/> that manages connections to remote MCP servers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCoreAIMcpClient(this IServiceCollection services, bool includeStdIoTransport = true)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCoreAIMcpServices();

        if (includeStdIoTransport)
        {
            services.AddCoreAIStdIoMcpClientTransport();
        }

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAICompletionContextBuilderHandler, McpAICompletionContextBuilderHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IToolRegistryProvider, McpToolRegistryProvider>());
        services.AddCoreAITool<McpInvokeFunction>(McpInvokeFunction.FunctionName);

        return services;
    }

    public static CrestAppsAISuiteBuilder AddMcpServices(this CrestAppsAISuiteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIMcpServices();
        return builder;
    }

    public static CrestAppsAISuiteBuilder AddMcpClient(this CrestAppsAISuiteBuilder builder, Action<CrestAppsMcpClientBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIMcpClient();

        if (configure is not null)
        {
            configure(new CrestAppsMcpClientBuilder(builder.Services));
        }

        return builder;
    }

    /// <summary>
    /// Adds MCP server services for serving prompts and resources through the
    /// Model Context Protocol. Call this when your application acts as an MCP server.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCoreAIMcpServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IMcpServerPromptService, DefaultMcpServerPromptService>();
        services.TryAddScoped<IMcpServerResourceService, DefaultMcpServerResourceService>();

        return services;
    }

    public static CrestAppsAISuiteBuilder AddMcpServer(this CrestAppsAISuiteBuilder builder, Action<CrestAppsMcpServerBuilder> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCoreAIMcpServer();

        if (configure is not null)
        {
            configure(new CrestAppsMcpServerBuilder(builder.Services));
        }

        return builder;
    }

    /// <summary>
    /// Registers an MCP resource type with its handler.
    /// </summary>
    /// <typeparam name="THandler">The type of handler for this resource type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="type">The unique type identifier for this resource type.</param>
    /// <param name="configure">Optional configuration action for the resource type entry.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddCoreAIMcpResourceType<THandler>(
        this IServiceCollection services,
        string type,
        Action<McpResourceTypeEntry> configure = null)
        where THandler : class, IMcpResourceTypeHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(type);

        services.Configure<McpOptions>(options =>
        {
            options.AddResourceType(type, configure);
        });

        // Register the handler implementation
        services.AddScoped<THandler>();

        // Register by interface for enumeration
        services.AddScoped<IMcpResourceTypeHandler>(sp => sp.GetRequiredService<THandler>());

        // Register as keyed service for direct lookup by type
        services.AddKeyedScoped<IMcpResourceTypeHandler>(type, (sp, key) => sp.GetRequiredService<THandler>());

        return services;
    }
}
