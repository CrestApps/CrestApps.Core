using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Covers the Elasticsearch service registrations a host can call on their own.
/// </summary>
public sealed class ElasticsearchServiceCollectionExtensionsTests
{
    /// <summary>
    /// Verifies that a host that brings its own Elasticsearch services can register the filter translator
    /// alone, so no client, index manager or content manager is registered beside it.
    /// </summary>
    [Fact]
    public void AddElasticsearchODataFilterTranslator_RegistersOnlyTheKeyedTranslator()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddElasticsearchODataFilterTranslator();

        // Assert
        var descriptor = Assert.Single(services);

        Assert.True(descriptor.IsKeyedService);
        Assert.Equal(ElasticsearchConstants.ProviderName, descriptor.ServiceKey);
        Assert.Equal(typeof(IODataFilterTranslator), descriptor.ServiceType);

        using var provider = services.BuildServiceProvider();
        var translator = provider.GetRequiredKeyedService<IODataFilterTranslator>(ElasticsearchConstants.ProviderName);

        Assert.Equal("""{"term":{"filters.category":"docs"}}""", translator.Translate("category eq 'docs'"));
    }

    /// <summary>
    /// Verifies that calling the translator registration twice, for example once directly and once through
    /// <c>AddCoreElasticsearchServices</c>, keeps one
    /// translator.
    /// </summary>
    [Fact]
    public void AddElasticsearchODataFilterTranslator_WhenCalledWithTheCoreServices_RegistersOneTranslator()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddElasticsearchODataFilterTranslator();
        services.AddCoreElasticsearchServices();

        // Assert
        Assert.Single(services, descriptor =>
            descriptor.IsKeyedService &&
            Equals(descriptor.ServiceKey, ElasticsearchConstants.ProviderName) &&
            descriptor.ServiceType == typeof(IODataFilterTranslator));
    }
}
