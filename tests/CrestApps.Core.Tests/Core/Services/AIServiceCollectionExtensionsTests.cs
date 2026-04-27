using CrestApps.Core.AI;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Data.EntityCore;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Indexes.AI;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YesSql;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class AIServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCoreAIServices_DoesNotRegisterGenericConnectionOrDeploymentCatalogs()
    {
        var services = CreateBaseServices();
        services.AddCoreAIServices();

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServices = scope.ServiceProvider;

        Assert.IsType<DefaultAIProviderConnectionStore>(scopedServices.GetRequiredService<IAIProviderConnectionStore>());
        Assert.IsType<DefaultAIDeploymentStore>(scopedServices.GetRequiredService<IAIDeploymentStore>());
        Assert.Null(scopedServices.GetService<INamedSourceCatalog<AIProviderConnection>>());
        Assert.Null(scopedServices.GetService<INamedCatalog<AIProviderConnection>>());
        Assert.Null(scopedServices.GetService<ISourceCatalog<AIProviderConnection>>());
        Assert.Null(scopedServices.GetService<ICatalog<AIProviderConnection>>());
        Assert.Null(scopedServices.GetService<INamedSourceCatalog<AIDeployment>>());
        Assert.Null(scopedServices.GetService<INamedCatalog<AIDeployment>>());
        Assert.Null(scopedServices.GetService<ISourceCatalog<AIDeployment>>());
        Assert.Null(scopedServices.GetService<ICatalog<AIDeployment>>());
    }

    [Fact]
    public void AddCoreAIServices_RegistersFrameworkAIProfileManager_WhenProfileCatalogIsAvailable()
    {
        var services = CreateBaseServices();
        services.AddScoped(_ => Mock.Of<INamedCatalog<AIProfile>>());
        services.AddCoreAIServices();

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServices = scope.ServiceProvider;

        Assert.IsType<DefaultAIProfileManager>(scopedServices.GetRequiredService<IAIProfileManager>());
        Assert.IsType<DefaultAIProfileManager>(scopedServices.GetRequiredService<ICatalogManager<AIProfile>>());
        Assert.IsType<DefaultAIProfileManager>(scopedServices.GetRequiredService<INamedCatalogManager<AIProfile>>());
    }

    [Fact]
    public void AddCoreAIServicesStoresEntityCore_RegistersDatabaseCatalogInterfaces()
    {
        var services = CreateBaseServices();
        services.AddCoreAIServices();
        services.AddCoreEntityCoreSqliteDataStore("Data Source=:memory:");
        services.AddCoreAIServicesStoresEntityCore();

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServices = scope.ServiceProvider;

        Assert.IsType<DefaultAIProviderConnectionStore>(scopedServices.GetRequiredService<IAIProviderConnectionStore>());
        Assert.IsType<DefaultAIDeploymentStore>(scopedServices.GetRequiredService<IAIDeploymentStore>());
        Assert.IsType<DefaultAIProfileManager>(scopedServices.GetRequiredService<IAIProfileManager>());
        Assert.IsType<Data.EntityCore.Services.EntityCoreAIProfileStore>(scopedServices.GetRequiredService<IAIProfileStore>());
        Assert.IsType<Data.EntityCore.Services.EntityCoreAIProfileStore>(scopedServices.GetRequiredService<INamedSourceCatalog<AIProfile>>());
        Assert.IsType<Data.EntityCore.Services.EntityCoreAIProfileStore>(scopedServices.GetRequiredService<INamedCatalog<AIProfile>>());
        Assert.IsType<Data.EntityCore.Services.EntityCoreAIProfileStore>(scopedServices.GetRequiredService<ISourceCatalog<AIProfile>>());
        Assert.IsType<Data.EntityCore.Services.EntityCoreAIProfileStore>(scopedServices.GetRequiredService<ICatalog<AIProfile>>());
        Assert.IsType<Data.EntityCore.Services.NamedSourceDocumentCatalog<AIProviderConnection>>(scopedServices.GetRequiredService<INamedSourceCatalog<AIProviderConnection>>());
        Assert.IsType<Data.EntityCore.Services.NamedSourceDocumentCatalog<AIProviderConnection>>(scopedServices.GetRequiredService<INamedCatalog<AIProviderConnection>>());
        Assert.IsType<Data.EntityCore.Services.NamedSourceDocumentCatalog<AIProviderConnection>>(scopedServices.GetRequiredService<ISourceCatalog<AIProviderConnection>>());
        Assert.IsType<Data.EntityCore.Services.NamedSourceDocumentCatalog<AIProviderConnection>>(scopedServices.GetRequiredService<ICatalog<AIProviderConnection>>());
        Assert.IsType<Data.EntityCore.Services.NamedSourceDocumentCatalog<AIDeployment>>(scopedServices.GetRequiredService<INamedSourceCatalog<AIDeployment>>());
        Assert.IsType<Data.EntityCore.Services.NamedSourceDocumentCatalog<AIDeployment>>(scopedServices.GetRequiredService<INamedCatalog<AIDeployment>>());
        Assert.IsType<Data.EntityCore.Services.NamedSourceDocumentCatalog<AIDeployment>>(scopedServices.GetRequiredService<ISourceCatalog<AIDeployment>>());
        Assert.IsType<Data.EntityCore.Services.NamedSourceDocumentCatalog<AIDeployment>>(scopedServices.GetRequiredService<ICatalog<AIDeployment>>());
    }

    [Fact]
    public void AddCoreAIServicesStoresYesSql_RegistersDatabaseCatalogInterfaces()
    {
        var services = CreateBaseServices();
        services.AddScoped(_ => Mock.Of<ISession>());
        services.AddOptions<YesSqlStoreOptions>();
        services.AddCoreAIServices();
        services.AddCoreAIServicesStoresYesSql();

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var scopedServices = scope.ServiceProvider;

        Assert.IsType<DefaultAIProviderConnectionStore>(scopedServices.GetRequiredService<IAIProviderConnectionStore>());
        Assert.IsType<DefaultAIDeploymentStore>(scopedServices.GetRequiredService<IAIDeploymentStore>());
        Assert.IsType<DefaultAIProfileManager>(scopedServices.GetRequiredService<IAIProfileManager>());
        Assert.IsType<Data.YesSql.Services.YesSqlAIProfileStore>(scopedServices.GetRequiredService<IAIProfileStore>());
        Assert.IsType<Data.YesSql.Services.YesSqlAIProfileStore>(scopedServices.GetRequiredService<INamedSourceCatalog<AIProfile>>());
        Assert.IsType<Data.YesSql.Services.YesSqlAIProfileStore>(scopedServices.GetRequiredService<INamedCatalog<AIProfile>>());
        Assert.IsType<Data.YesSql.Services.YesSqlAIProfileStore>(scopedServices.GetRequiredService<ISourceCatalog<AIProfile>>());
        Assert.IsType<Data.YesSql.Services.YesSqlAIProfileStore>(scopedServices.GetRequiredService<ICatalog<AIProfile>>());
        Assert.IsType<Data.YesSql.Services.NamedSourceDocumentCatalog<AIProviderConnection, AIProviderConnectionIndex>>(scopedServices.GetRequiredService<INamedSourceCatalog<AIProviderConnection>>());
        Assert.IsType<Data.YesSql.Services.NamedSourceDocumentCatalog<AIProviderConnection, AIProviderConnectionIndex>>(scopedServices.GetRequiredService<INamedCatalog<AIProviderConnection>>());
        Assert.IsType<Data.YesSql.Services.NamedSourceDocumentCatalog<AIProviderConnection, AIProviderConnectionIndex>>(scopedServices.GetRequiredService<ISourceCatalog<AIProviderConnection>>());
        Assert.IsType<Data.YesSql.Services.NamedSourceDocumentCatalog<AIProviderConnection, AIProviderConnectionIndex>>(scopedServices.GetRequiredService<ICatalog<AIProviderConnection>>());
        Assert.IsType<Data.YesSql.Services.NamedSourceDocumentCatalog<AIDeployment, AIDeploymentIndex>>(scopedServices.GetRequiredService<INamedSourceCatalog<AIDeployment>>());
        Assert.IsType<Data.YesSql.Services.NamedSourceDocumentCatalog<AIDeployment, AIDeploymentIndex>>(scopedServices.GetRequiredService<INamedCatalog<AIDeployment>>());
        Assert.IsType<Data.YesSql.Services.NamedSourceDocumentCatalog<AIDeployment, AIDeploymentIndex>>(scopedServices.GetRequiredService<ISourceCatalog<AIDeployment>>());
        Assert.IsType<Data.YesSql.Services.NamedSourceDocumentCatalog<AIDeployment, AIDeploymentIndex>>(scopedServices.GetRequiredService<ICatalog<AIDeployment>>());
    }

    private static ServiceCollection CreateBaseServices()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddLocalization();
        services.AddOptions();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        return services;
    }
}
