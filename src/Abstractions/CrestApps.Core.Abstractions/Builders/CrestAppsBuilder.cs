using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddCrestAppsCore</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering core framework services.
/// </summary>
public sealed class CrestAppsCoreBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsCoreBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsCoreBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register core framework services.
    /// </summary>
    public IServiceCollection Services { get; }
}

/// <summary>
/// Builder returned by <c>AddCrestAppsAISuite</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering AI suite services.
/// </summary>
public sealed class CrestAppsAISuiteBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsAISuiteBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsAISuiteBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register AI suite services.
    /// </summary>
    public IServiceCollection Services { get; }
}

/// <summary>
/// Builder returned by <c>AddCrestAppsIndexing</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering search indexing services.
/// </summary>
public sealed class CrestAppsIndexingBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsIndexingBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsIndexingBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register search indexing services.
    /// </summary>
    public IServiceCollection Services { get; }
}

/// <summary>
/// Builder returned by <c>AddCrestAppsChatInteractions</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering chat interaction services.
/// </summary>
public sealed class CrestAppsChatInteractionsBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsChatInteractionsBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsChatInteractionsBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register chat interaction services.
    /// </summary>
    public IServiceCollection Services { get; }
}

/// <summary>
/// Builder returned by <c>AddCrestAppsDocumentProcessing</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering document processing services.
/// </summary>
public sealed class CrestAppsDocumentProcessingBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsDocumentProcessingBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsDocumentProcessingBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register document processing services.
    /// </summary>
    public IServiceCollection Services { get; }
}

/// <summary>
/// Builder returned by <c>AddCrestAppsA2AClient</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering Agent-to-Agent (A2A) client services.
/// </summary>
public sealed class CrestAppsA2AClientBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsA2AClientBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsA2AClientBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register A2A client services.
    /// </summary>
    public IServiceCollection Services { get; }
}

/// <summary>
/// Builder returned by <c>AddCrestAppsMcpClient</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering Model Context Protocol (MCP) client services.
/// </summary>
public sealed class CrestAppsMcpClientBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsMcpClientBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsMcpClientBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register MCP client services.
    /// </summary>
    public IServiceCollection Services { get; }
}

/// <summary>
/// Builder returned by <c>AddCrestAppsMcpServer</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering Model Context Protocol (MCP) server services.
/// </summary>
public sealed class CrestAppsMcpServerBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsMcpServerBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsMcpServerBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register MCP server services.
    /// </summary>
    public IServiceCollection Services { get; }
}

/// <summary>
/// Builder returned by <c>AddCrestAppsAIMemory</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering AI memory services.
/// </summary>
public sealed class CrestAppsAIMemoryBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsAIMemoryBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsAIMemoryBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register AI memory services.
    /// </summary>
    public IServiceCollection Services { get; }
}
