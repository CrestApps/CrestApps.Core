using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;

namespace CrestApps.Core.Tests.Core.Mcp;

public sealed class DefaultMcpServerResourceServiceTests
{
    [Fact]
    public async Task ListAsync_MergesResourcesInPrecedenceOrderAndRemovesDuplicateUris()
    {
        var catalogResources = new[]
        {
            CreateCatalogResource("catalog", "resource://catalog"),
            CreateCatalogResource("duplicate", "resource://duplicate"),
        };
        var providerResources = new[]
        {
            CreateServerResource("duplicate", "resource://duplicate"),
            CreateServerResource("provider", "resource://provider"),
        };
        var sdkResources = new[]
        {
            CreateServerResource("provider-duplicate", "resource://provider"),
            CreateServerResource("sdk", "resource://sdk"),
        };

        var service = CreateService(catalogResources, providerResources, sdkResources);

        var resources = await service.ListAsync();

        Assert.Equal(
            ["resource://catalog", "resource://duplicate", "resource://provider", "resource://sdk"],
            resources.Select(resource => resource.Uri));
    }

    [Fact]
    public async Task ListTemplatesAsync_ReturnsOnlyTemplateResources()
    {
        var catalogResources = new[]
        {
            CreateCatalogResource("concrete", "resource://items/current"),
            CreateCatalogResource("template", "resource://items/{id}"),
        };
        var service = CreateService(catalogResources, [], []);

        var resources = await service.ListAsync();
        var templates = await service.ListTemplatesAsync();

        var resource = Assert.Single(resources);
        Assert.Equal("resource://items/current", resource.Uri);

        var template = Assert.Single(templates);
        Assert.Equal("resource://items/{id}", template.UriTemplate);
        Assert.Equal("template", template.Name);
    }

    private static DefaultMcpServerResourceService CreateService(
        IReadOnlyCollection<McpResource> catalogResources,
        IReadOnlyList<McpServerResource> providerResources,
        IReadOnlyList<McpServerResource> sdkResources)
    {
        var catalog = new Mock<ISourceCatalog<McpResource>>();
        catalog
            .Setup(instance => instance.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogResources);

        var provider = new Mock<IMcpResourceProvider>();
        provider
            .Setup(instance => instance.GetResourcesAsync())
            .ReturnsAsync(providerResources);

        return new DefaultMcpServerResourceService(catalog.Object, [provider.Object], sdkResources);
    }

    private static McpResource CreateCatalogResource(string name, string uri)
    {
        return new McpResource
        {
            ItemId = name,
            Source = "test",
            Resource = new Resource
            {
                Name = name,
                Uri = uri,
            },
        };
    }

    private static McpServerResource CreateServerResource(string name, string uri)
    {
        return McpServerResource.Create(
            (Func<string>)(() => string.Empty),
            new McpServerResourceCreateOptions
            {
                Name = name,
                UriTemplate = uri,
            });
    }
}
