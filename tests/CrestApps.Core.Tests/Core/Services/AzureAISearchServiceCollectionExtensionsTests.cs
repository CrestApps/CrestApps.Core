using CrestApps.Core.Azure.AISearch;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Covers the Azure AI Search service registrations a host can call on their own.
/// </summary>
public sealed class AzureAISearchServiceCollectionExtensionsTests
{
    /// <summary>
    /// Verifies that a host that brings its own Azure AI Search services can register the filter translator
    /// alone, so no client, index manager or content manager is registered beside it.
    /// </summary>
    [Fact]
    public void AddAzureAISearchODataFilterTranslator_RegistersOnlyTheKeyedTranslator()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddAzureAISearchODataFilterTranslator();

        // Assert
        var descriptor = Assert.Single(services);

        Assert.True(descriptor.IsKeyedService);
        Assert.Equal(AISearchConstants.ProviderName, descriptor.ServiceKey);
        Assert.Equal(typeof(IODataFilterTranslator), descriptor.ServiceType);

        using var provider = services.BuildServiceProvider();
        var translator = provider.GetRequiredKeyedService<IODataFilterTranslator>(AISearchConstants.ProviderName);

        Assert.Equal("filters/category eq 'docs'", translator.Translate("category eq 'docs'"));
    }

    /// <summary>
    /// Verifies that calling the translator registration twice, for example once directly and once through
    /// <c>AddCoreAzureAISearchServices</c>, keeps one
    /// translator.
    /// </summary>
    [Fact]
    public void AddAzureAISearchODataFilterTranslator_WhenCalledWithTheCoreServices_RegistersOneTranslator()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddAzureAISearchODataFilterTranslator();
        services.AddCoreAzureAISearchServices();

        // Assert
        Assert.Single(services, descriptor =>
            descriptor.IsKeyedService &&
            Equals(descriptor.ServiceKey, AISearchConstants.ProviderName) &&
            descriptor.ServiceType == typeof(IODataFilterTranslator));
    }
}
