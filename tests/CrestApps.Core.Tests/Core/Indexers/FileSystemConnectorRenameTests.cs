using CrestApps.Core.AI.FileSources;
using CrestApps.Core.AI.FileSources.Connectors;
using CrestApps.Core.AI.Indexing;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Blazor.Web.Components.Pages.FileSources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using BlazorFileSourceViewModel = CrestApps.Core.Blazor.Web.ViewModels.FileSourceViewModel;
using MvcFileSourceViewModel = CrestApps.Core.Mvc.Web.Areas.FileSources.ViewModels.FileSourceViewModel;

namespace CrestApps.Core.Tests.Core.Indexers;

/// <summary>
/// Covers the one thing a connector rename can break: a file source stores its connector's name, so every
/// record written before the rename still says <c>LocalFolder</c>. Nothing migrates those records, so the
/// old name has to keep working everywhere a stored source is read — resolving the connector to run it,
/// deciding which screen the record belongs on, and filling the form that edits it.
/// </summary>
public sealed class FileSystemConnectorRenameTests
{
    /// <summary>
    /// Verifies that the connector resolves under the name it is registered as today and under the one it
    /// was registered as before. Without this a file source stored before the rename resolves to nothing,
    /// and its scheduled runs stop with no folder ever being read again.
    /// </summary>
    /// <remarks>
    /// Both names are asserted exactly as they are written, because a keyed service is looked up by an
    /// exact key. Nothing stores a source in any other casing: it is always the constant the code that
    /// saved the record wrote.
    /// </remarks>
    [Theory]
    [InlineData(FileSystemIngestionConnector.ConnectorName)]
    [InlineData(FileSystemIngestionConnector.DeprecatedConnectorName)]
    public void Resolve_EitherName_ReturnsTheFileSystemConnector(string source)
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var connector = scope.ServiceProvider.GetRequiredService<IIngestionConnectorResolver>().Get(source);

        Assert.IsType<FileSystemIngestionConnector>(connector);
    }

    /// <summary>
    /// Verifies that the old name is a key only. It resolves, but the picker still offers one entry, or the
    /// same connector would be listed twice under two names.
    /// </summary>
    [Fact]
    public void Connectors_ListTheFileSystemConnectorOnce()
    {
        using var provider = CreateProvider();

        var connectors = provider.GetRequiredService<IOptions<IngestionConnectorOptions>>().Value.Connectors;

        var descriptor = Assert.Single(connectors);
        Assert.Equal(FileSystemIngestionConnector.ConnectorName, descriptor.Name);
        Assert.Equal("File system", descriptor.DisplayName.Value);
        Assert.Contains(FileSystemIngestionConnector.DeprecatedConnectorName, descriptor.Aliases);
    }

    /// <summary>
    /// Verifies that a record stored under the old name is still recognised as connector-backed. A record
    /// that matches neither a connector nor a crawl strategy belongs to no screen at all, so failing this
    /// would make every folder file source disappear from the application rather than fail visibly.
    /// </summary>
    [Fact]
    public void IsIngestionConnector_DeprecatedName_IsStillAConnector()
    {
        using var provider = CreateProvider();

        var connectors = provider.GetRequiredService<IOptions<IngestionConnectorOptions>>().Value.Connectors;

        Assert.True(FileSourceFilter.IsIngestionConnector(FileSystemIngestionConnector.DeprecatedConnectorName, connectors));
        Assert.True(FileSourceFilter.IsIngestionConnector(FileSystemIngestionConnector.ConnectorName, connectors));
        Assert.False(FileSourceFilter.IsIngestionConnector("SomethingNoLongerRegistered", connectors));
    }

    /// <summary>
    /// Verifies that both hosts' forms read a record stored under the old name as the connector it is now.
    /// The picker has no entry for the old name, so leaving it as stored would leave the form showing
    /// whichever connector happened to be first and quietly move the record there on the next save.
    /// </summary>
    [Fact]
    public void FromFileSource_DeprecatedName_ReadsAsTheCurrentName()
    {
        var fileSource = new WebCrawler
        {
            ItemId = "file-source-1",
            Source = FileSystemIngestionConnector.DeprecatedConnectorName,
            DisplayText = "Knowledge folder",
            AIDataSourceId = "data-source-1",
        };

        fileSource.Put(new LocalFolderIndexerMetadata
        {
            RootPath = "D:\\knowledge",
            SearchPattern = "*.pdf",
            Recursive = false,
        });

        var mvc = MvcFileSourceViewModel.FromFileSource(fileSource);

        Assert.Equal(FileSystemIngestionConnector.ConnectorName, mvc.Source);
        Assert.True(mvc.IsFileSystem);
        Assert.Equal("D:\\knowledge", mvc.LocalRootPath);

        var blazor = BlazorFileSourceViewModel.FromFileSource(fileSource);

        Assert.Equal(FileSystemIngestionConnector.ConnectorName, blazor.Source);
        Assert.True(blazor.IsFileSystem);
        Assert.Equal("D:\\knowledge", blazor.LocalRootPath);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddCoreFileSystemConnector();

        return services.BuildServiceProvider();
    }
}
