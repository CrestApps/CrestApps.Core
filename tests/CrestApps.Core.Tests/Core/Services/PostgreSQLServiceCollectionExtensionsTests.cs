using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.PostgreSQL;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.PostgreSQL;
using CrestApps.Core.PostgreSQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class PostgreSQLServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCorePostgreSQLAIDataSource_RegistersDataSourceSyncServices()
    {
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddCorePostgreSQLAIDataSource();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IAIDataSourceIndexingQueue) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ISearchDocumentHandler) &&
            descriptor.ImplementationType == typeof(AIDataSourceSearchDocumentHandler));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IIndexProfileHandler) &&
            descriptor.ImplementationType == typeof(DataSourceSearchIndexProfileHandler));

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<IndexProfileSourceOptions>>().Value;

        Assert.Contains(options.Sources, source =>
            source.ProviderName == PostgreSQLConstants.ProviderName &&
            source.Type == IndexProfileTypes.DataSource);
        Assert.Contains(serviceProvider.GetRequiredService<IOptions<AIDataSourceSourceOptions>>().Value.Sources, source =>
            source.SourceType == AIDataSourceSourceTypes.PostgreSQL);
    }

    [Fact]
    public void IndexProfileSourceOptions_AddOrUpdate_StringOverload_ShouldRemainCompatible()
    {
        var options = new IndexProfileSourceOptions();

        options.AddOrUpdate(PostgreSQLConstants.ProviderName, "PostgreSQL", IndexProfileTypes.DataSource, descriptor =>
        {
            descriptor.DisplayName = "Data Source";
            descriptor.Description = "Compatibility overload";
        });

        var source = Assert.Single(options.Sources);
        Assert.Equal(PostgreSQLConstants.ProviderName, source.ProviderName);
        Assert.Equal("PostgreSQL", source.ProviderDisplayName);
        Assert.Equal(IndexProfileTypes.DataSource, source.Type);
        Assert.Equal("Data Source", source.DisplayName);
    }

    [Fact]
    public void AddCorePostgreSQLAIDocumentSource_RegistersDocumentServices()
    {
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddCorePostgreSQLAIDocumentSource();

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<IndexProfileSourceOptions>>().Value;

        Assert.Contains(options.Sources, source =>
            source.ProviderName == PostgreSQLConstants.ProviderName &&
            source.Type == IndexProfileTypes.AIDocuments);
    }

    [Fact]
    public void AddCorePostgreSQLAIMemorySource_RegistersMemoryServices()
    {
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddCorePostgreSQLAIMemorySource();

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<IndexProfileSourceOptions>>().Value;

        Assert.Contains(options.Sources, source =>
            source.ProviderName == PostgreSQLConstants.ProviderName &&
            source.Type == IndexProfileTypes.AIMemory);
    }

    [Fact]
    public void PostgreSQLClientFactory_Create_ShouldCacheConfiguredClient()
    {
        var factory = new PostgreSQLClientFactory(
            NullLogger<PostgreSQLClientFactory>.Instance,
            Options.Create(new PostgreSQLConnectionOptions
            {
                ConnectionString = "Host=localhost;Database=test;Username=postgres;Password=test",
            }));

        var firstClient = factory.Create();
        var secondClient = factory.Create();

        Assert.Same(firstClient, secondClient);
    }

    [Fact]
    public void PostgreSQLClientFactory_Create_ShouldRequireConnectionString()
    {
        var factory = new PostgreSQLClientFactory(
            NullLogger<PostgreSQLClientFactory>.Instance,
            Options.Create(new PostgreSQLConnectionOptions()));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Create());

        Assert.Contains("connection string", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddCorePostgreSQLServices_RegistersKeyedServices()
    {
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddLogging();
        services.Configure<PostgreSQLConnectionOptions>(options =>
        {
            options.ConnectionString = "Host=localhost;Database=test";
        });
        services.AddCorePostgreSQLServices();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPostgreSQLClientFactory) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);

        Assert.Contains(services, descriptor =>
            descriptor.IsKeyedService &&
            descriptor.ServiceKey as string == PostgreSQLConstants.ProviderName &&
            descriptor.ServiceType == typeof(ISearchIndexManager));

        Assert.Contains(services, descriptor =>
            descriptor.IsKeyedService &&
            descriptor.ServiceKey as string == PostgreSQLConstants.ProviderName &&
            descriptor.ServiceType == typeof(ISearchDocumentManager));

        Assert.Contains(services, descriptor =>
            descriptor.IsKeyedService &&
            descriptor.ServiceKey as string == PostgreSQLConstants.ProviderName &&
            descriptor.ServiceType == typeof(IODataFilterTranslator));
    }
}
