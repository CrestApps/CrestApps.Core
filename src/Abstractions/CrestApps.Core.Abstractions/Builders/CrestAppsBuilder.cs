using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddCrestAppsCore</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering core framework services.
/// </summary>
public sealed class CrestAppsCoreBuilder
{
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
