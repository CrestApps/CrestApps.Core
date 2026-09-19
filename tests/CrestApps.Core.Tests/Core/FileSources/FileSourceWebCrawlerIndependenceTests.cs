using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.EntityCore;
using CrestApps.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.FileSources;

/// <summary>
/// Covers the rule that the file-sources feature and the web-crawlers feature do not depend on each other.
/// </summary>
/// <remarks>
/// Either may be enabled without the other, so neither may reach for a store, a handler or a service the
/// other registers. What they share — running an ingestion source — belongs to the ingestion package that
/// both depend on, never to one of them. These assert against the service collection rather than a built
/// provider, because constructing an EntityCore store needs a database and the question here is only which
/// services a host is offered.
/// </remarks>
public sealed class FileSourceWebCrawlerIndependenceTests
{
    /// <summary>
    /// Verifies that enabling file source stores on their own registers the knowledge object store.
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
    /// Verifies that enabling file source stores on their own does not drag the web crawler stores in.
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

    /// <summary>
    /// Verifies that the shared ingestion runtime is safe for both features to register.
    /// </summary>
    /// <remarks>
    /// Running a source is the one thing the two features have in common, so both call this. Registering the
    /// run service twice would leave the container resolving whichever descriptor came last.
    /// </remarks>
    [Fact]
    public void IngestionRuntime_IsRegisteredOnceWhenBothFeaturesAskForIt()
    {
        var services = new ServiceCollection();

        services.AddCoreAIIngestionRuntime();
        services.AddCoreAIIngestionRuntime();

        Assert.Single(services, service => service.ServiceType == typeof(IIngestionRunService));
        Assert.Single(services, service => service.ServiceType == typeof(IIngestionConnectorResolver));
    }

    /// <summary>
    /// Verifies that the shared ingestion runtime names neither feature's records.
    /// </summary>
    /// <remarks>
    /// The pipeline reaches a source record only through a contributed <see cref="IIngestionSourceProvider"/>,
    /// so the package both features share must not register a provider of its own for either record type.
    /// </remarks>
    [Fact]
    public void IngestionRuntime_ContributesNoSourceProviderOfItsOwn()
    {
        var services = new ServiceCollection();

        services.AddCoreAIIngestionRuntime();

        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IIngestionSourceProvider));
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IWebCrawlerStore));
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IFileSourceStore));
    }
}
