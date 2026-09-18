using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.FileSources;
using CrestApps.Core.AI.FileSources.Handlers;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;
using Moq;

namespace CrestApps.Core.Tests.Core.Indexers;

/// <summary>
/// Covers the settings an indexer carries, and the ones it refuses to carry. A deployment that cannot do the
/// job it was chosen for is not a cheaper option: it is a setting that silently does nothing.
/// </summary>
public sealed class IndexerSettingsTests
{
    /// <summary>
    /// Verifies that a deployment that cannot accept an image is refused as the vision deployment, at the
    /// point where someone is there to read the message.
    /// </summary>
    [Fact]
    public async Task Handler_VisionDeploymentWithoutImageInput_IsRejected()
    {
        var context = CreateContext(new IndexerMetadata
        {
            VisionDeploymentName = "text-only",
        });

        await CreateHandler(imageInput: false, textEmbedding: true).ValidatingAsync(context, TestContext.Current.CancellationToken);

        Assert.False(context.Result.Succeeded);
        Assert.Contains(context.Result.Errors, error => error.ErrorMessage.Contains("image input", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that a deployment nobody chose is valid. Leaving one unset means "use the application's",
    /// never "disable": <c>FigureMode</c> is how figure transcription is turned off.
    /// </summary>
    [Fact]
    public async Task Handler_NullDeployments_AreValid()
    {
        var context = CreateContext(new IndexerMetadata());

        await CreateHandler(imageInput: false, textEmbedding: false).ValidatingAsync(context, TestContext.Current.CancellationToken);

        Assert.True(context.Result.Succeeded);
    }

    /// <summary>
    /// Verifies that a deployment that no longer exists is refused rather than accepted and silently unused.
    /// </summary>
    [Fact]
    public async Task Handler_DeletedDeployment_IsRejected()
    {
        var context = CreateContext(new IndexerMetadata
        {
            VisionDeploymentName = "deleted",
        });

        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager
            .Setup(manager => manager.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AIDeployment)null);

        var handler = new FileSourceSettingsCatalogHandler(deploymentManager.Object, Mock.Of<IAIDeploymentCapabilityService>());

        await handler.ValidatingAsync(context, TestContext.Current.CancellationToken);

        Assert.False(context.Result.Succeeded);
    }

    /// <summary>
    /// Verifies that an indexer carrying no ingestion settings at all validates, so every crawler that
    /// existed before these settings did keeps saving.
    /// </summary>
    [Fact]
    public async Task Handler_NoIndexerSettings_IsValid()
    {
        var context = new ValidatingContext<WebCrawler>(new WebCrawler
        {
            ItemId = "indexer-1",
            Source = "Sitemap",
        });

        await CreateHandler(imageInput: false, textEmbedding: false).ValidatingAsync(context, TestContext.Current.CancellationToken);

        Assert.True(context.Result.Succeeded);
    }

    private static ValidatingContext<WebCrawler> CreateContext(IndexerMetadata metadata)
    {
        var indexer = new WebCrawler
        {
            ItemId = "indexer-1",
            Source = "FileSystem",
            DisplayText = "The folder",
            AIDataSourceId = "data-source-1",
        };

        indexer.Put(metadata);

        return new ValidatingContext<WebCrawler>(indexer);
    }

    private static FileSourceSettingsCatalogHandler CreateHandler(bool imageInput, bool textEmbedding)
    {
        var deployment = new AIDeployment
        {
            ItemId = "deployment-1",
            Name = "a-deployment",
        };

        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager
            .Setup(manager => manager.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(deployment);

        var capabilityService = new Mock<IAIDeploymentCapabilityService>();
        capabilityService
            .Setup(service => service.SupportsFeatureOrUnconstrained(It.IsAny<AIDeployment>(), AIDeploymentFeatureNames.ImageInput))
            .Returns(imageInput);
        capabilityService
            .Setup(service => service.SupportsFeatureOrUnconstrained(It.IsAny<AIDeployment>(), AIDeploymentFeatureNames.TextEmbedding))
            .Returns(textEmbedding);

        return new FileSourceSettingsCatalogHandler(deploymentManager.Object, capabilityService.Object);
    }
}
