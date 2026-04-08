using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

public class CrestAppsCoreBuilder
{
    public CrestAppsCoreBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IServiceCollection Services { get; }
}

public sealed class CrestAppsAISuiteBuilder
{
    public CrestAppsAISuiteBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IServiceCollection Services { get; }
}

public sealed class CrestAppsIndexingBuilder
{
    public CrestAppsIndexingBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IServiceCollection Services { get; }
}

public sealed class CrestAppsChatInteractionsBuilder
{
    public CrestAppsChatInteractionsBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IServiceCollection Services { get; }
}

public sealed class CrestAppsDocumentProcessingBuilder
{
    public CrestAppsDocumentProcessingBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IServiceCollection Services { get; }
}

public sealed class CrestAppsMcpServerBuilder
{
    public CrestAppsMcpServerBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IServiceCollection Services { get; }
}
