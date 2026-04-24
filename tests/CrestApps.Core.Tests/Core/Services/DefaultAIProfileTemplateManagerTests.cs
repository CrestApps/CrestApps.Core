using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

public sealed class DefaultAIProfileTemplateManagerTests
{
    [Fact]
    public async Task GetAllAsync_MergesDatabaseAndProviderTemplates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var store = new Mock<INamedSourceCatalog<AIProfileTemplate>>();
        var dbTemplate = new AIProfileTemplate { ItemId = "db-1", Name = "db-template", Source = AITemplateSources.Profile, IsListable = true };
        var duplicateTemplate = new AIProfileTemplate { ItemId = "db-2", Name = "shared-name", Source = AITemplateSources.Profile, IsListable = true };
        var providerTemplate = new AIProfileTemplate { ItemId = "file-1", Name = "shared-name", Source = AITemplateSources.Profile, IsListable = true };
        var uniqueProviderTemplate = new AIProfileTemplate { ItemId = "file-2", Name = "file-template", Source = AITemplateSources.Profile, IsListable = true };
        var provider = new Mock<IAIProfileTemplateProvider>();

        store.Setup(catalog => catalog.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([dbTemplate, duplicateTemplate]);

        provider.Setup(p => p.GetTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([providerTemplate, uniqueProviderTemplate]);

        var manager = new DefaultAIProfileTemplateManager(store.Object, [], [provider.Object], NullLogger<DefaultAIProfileTemplateManager>.Instance);

        var templates = (await manager.GetAllAsync(cancellationToken)).ToList();

        Assert.Equal(3, templates.Count);
        Assert.Contains(templates, template => template.Name == "db-template");
        Assert.Contains(templates, template => template.Name == "shared-name" && template.ItemId == "db-2");
        Assert.Contains(templates, template => template.Name == "file-template");
    }

    [Fact]
    public async Task FindByIdAsync_FallsBackToProviderTemplates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var store = new Mock<INamedSourceCatalog<AIProfileTemplate>>();
        var providerTemplate = new AIProfileTemplate { ItemId = "file-1", Name = "file-template", Source = AITemplateSources.Profile, IsListable = true };
        var provider = new Mock<IAIProfileTemplateProvider>();

        store.Setup(catalog => catalog.FindByIdAsync("file-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AIProfileTemplate)null);

        provider.Setup(p => p.GetTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([providerTemplate]);

        var manager = new DefaultAIProfileTemplateManager(store.Object, [], [provider.Object], NullLogger<DefaultAIProfileTemplateManager>.Instance);

        var template = await manager.FindByIdAsync("file-1", cancellationToken);

        Assert.NotNull(template);
        Assert.Equal("file-template", template.Name);
    }

    [Fact]
    public async Task GetListableAsync_FiltersOutNonListableTemplates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var store = new Mock<INamedSourceCatalog<AIProfileTemplate>>();
        var provider = new Mock<IAIProfileTemplateProvider>();
        var visibleTemplate = new AIProfileTemplate { ItemId = "db-1", Name = "visible", Source = AITemplateSources.Profile, IsListable = true };
        var hiddenTemplate = new AIProfileTemplate { ItemId = "file-1", Name = "hidden", Source = AITemplateSources.Profile, IsListable = false };

        store.Setup(catalog => catalog.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([visibleTemplate]);

        provider.Setup(p => p.GetTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([hiddenTemplate]);

        var manager = new DefaultAIProfileTemplateManager(store.Object, [], [provider.Object], NullLogger<DefaultAIProfileTemplateManager>.Instance);

        var templates = (await manager.GetListableAsync(cancellationToken)).ToList();

        Assert.Single(templates);
        Assert.Equal("visible", templates[0].Name);
    }
}
