using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.EntityCore;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.FileSources;

/// <summary>
/// Covers what each feature's store registration has to bring with it.
/// </summary>
/// <remarks>
/// The file-sources feature and the web-crawlers feature are enabled independently, so neither may rely on
/// the other having been registered. These assert against the service collection rather than a built
/// provider, because constructing an EntityCore store needs a database and the question here is only which
/// services a host is offered.
/// </remarks>
public sealed class FileSourceStoreRegistrationTests
{
    /// <summary>
    /// Verifies that enabling file sources on its own registers the knowledge object store.
    /// </summary>
    /// <remarks>
    /// Knowledge objects are where an ingestion run puts the text, figures, charts and tables it read, so a
    /// file source cannot do its job without this store. It used to be registered only by the web crawlers
    /// feature, which meant a file-sources-only host had no way to store what it ingested.
    /// </remarks>
    [Fact]
    public void FileSourceStores_RegisterTheKnowledgeObjectStore()
    {
        var services = new ServiceCollection();

        services.AddCoreFileSourceStoresEntityCore();

        Assert.Contains(services, service => service.ServiceType == typeof(IKnowledgeObjectStore));
        Assert.Contains(services, service => service.ServiceType == typeof(IFileSourceStore));
        Assert.Contains(services, service => service.ServiceType == typeof(IIngestionItemStateStore));
    }

    /// <summary>
    /// Verifies that enabling file sources on its own does not drag the web crawler stores in with it.
    /// </summary>
    [Fact]
    public void FileSourceStores_DoNotRegisterTheWebCrawlerStores()
    {
        var services = new ServiceCollection();

        services.AddCoreFileSourceStoresEntityCore();

        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IWebCrawlerStore));
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IWebCrawlStateStore));
    }

    /// <summary>
    /// Verifies that the web crawlers feature still gets the knowledge object store it always had.
    /// </summary>
    [Fact]
    public void WebCrawlerStores_StillRegisterTheKnowledgeObjectStore()
    {
        var services = new ServiceCollection();

        services.AddCoreWebCrawlerStoresEntityCore();

        Assert.Contains(services, service => service.ServiceType == typeof(IKnowledgeObjectStore));
    }

    /// <summary>
    /// Verifies that a host enabling both features registers each shared service once.
    /// </summary>
    /// <remarks>
    /// Both features call the same knowledge registration, so it has to be safe to call twice. A duplicate
    /// catalog registration would leave two descriptors resolving the same store.
    /// </remarks>
    [Fact]
    public void BothFeatures_RegisterTheKnowledgeObjectStoreOnce()
    {
        var services = new ServiceCollection();

        services.AddCoreWebCrawlerStoresEntityCore();
        services.AddCoreFileSourceStoresEntityCore();

        Assert.Single(services, service => service.ServiceType == typeof(IKnowledgeObjectStore));
        Assert.Single(services, service => service.ServiceType == typeof(ICatalog<KnowledgeObject>));
        Assert.Single(services, service => service.ServiceType == typeof(ISourceCatalog<KnowledgeObject>));
    }
}
