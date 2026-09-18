using CrestApps.Core.AI.Mcp.Knowledge;
using CrestApps.Core.AI.Mcp.Knowledge.Handlers;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Services;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Moq;

namespace CrestApps.Core.Tests.Core.Mcp;

/// <summary>
/// Covers who may read an ingested figure over MCP. A canonical identifier is short and guessable, so the
/// handler has to establish that the object belongs to the data source in the address and is actually a
/// picture before it serves any bytes.
/// </summary>
public sealed class DataSourceFigureResourceHandlerTests
{
    private const string DataSourceId = "data-source-1";
    private const string FigureId = "figure:0123456789abcdef:1:0";
    private const string StoragePath = "figures/figure-1.png";

    /// <summary>
    /// Verifies that a figure in the addressed data source is served with its bytes and media type.
    /// </summary>
    [Fact]
    public async Task ReadAsync_HappyPath_ReturnsBlob()
    {
        var harness = new Harness();

        var result = await harness.ReadAsync(DataSourceId, FigureId);

        var blob = Assert.IsType<BlobResourceContents>(Assert.Single(result.Contents));

        Assert.Equal("image/png", blob.MimeType);
        Assert.Equal([1, 2, 3, 4], blob.Blob.ToArray());
    }

    /// <summary>
    /// Verifies that a figure requested through a data source it does not belong to is refused, so a guessed
    /// identifier cannot reach into a knowledge base the caller was never given.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WrongDataSource_IsRefused()
    {
        var harness = new Harness();

        var result = await harness.ReadAsync("data-source-2", FigureId);

        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));

        Assert.Equal("The figure was not found.", text.Text);
    }

    /// <summary>
    /// Verifies that an object that is not a picture is refused, because the type is what decides whether
    /// there are bytes to serve at all.
    /// </summary>
    [Fact]
    public async Task ReadAsync_NonFigureObject_IsRefused()
    {
        var harness = new Harness();

        harness.Store.Seed(new KnowledgeObject
        {
            ItemId = "text:0123456789abcdef:1:0",
            Source = DataSourceId,
            CanonicalId = "text:0123456789abcdef:1:0",
            ObjectType = KnowledgeObjectTypes.Text,
            Content = "Body.",
            StoragePath = StoragePath,
        });

        var result = await harness.ReadAsync(DataSourceId, "text:0123456789abcdef:1:0");

        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));

        Assert.Equal("The figure was not found.", text.Text);
    }

    /// <summary>
    /// Verifies that an address trying to climb out of its own scope is rejected before anything is looked
    /// up.
    /// </summary>
    [Fact]
    public async Task ReadAsync_Traversal_IsRejected()
    {
        var harness = new Harness();

        var result = await harness.ReadAsync(DataSourceId, "../../etc/passwd");

        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));

        Assert.Equal("The figure address is not valid.", text.Text);
    }

    /// <summary>
    /// Verifies that figures are published as a template only, so listing resources never enumerates every
    /// picture in a knowledge base.
    /// </summary>
    [Fact]
    public async Task ListResources_NeverContainsFigures()
    {
        var template = new McpResource
        {
            ItemId = "figures",
            Source = DataSourceFigureResourceConstants.Type,
            Resource = new Resource
            {
                Name = "Data source figure",
                Uri = DataSourceFigureResourceConstants.UriTemplate,
            },
        };

        var catalog = new Mock<ISourceCatalog<McpResource>>();
        catalog
            .Setup(instance => instance.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([template]);

        var service = new DefaultMcpServerResourceService(catalog.Object, [], []);

        Assert.Empty(await service.ListAsync());

        var published = Assert.Single(await service.ListTemplatesAsync());

        Assert.Equal(DataSourceFigureResourceConstants.UriTemplate, published.UriTemplate);
    }

    /// <summary>
    /// Assembles the handler over an in-memory store and file store.
    /// </summary>
    private sealed class Harness
    {
        public Harness()
        {
            Store.Seed(new KnowledgeObject
            {
                ItemId = FigureId,
                Source = DataSourceId,
                CanonicalId = FigureId,
                ObjectType = KnowledgeObjectTypes.Figure,
                Title = "Figure 1.",
                Content = "Figure 1.",
                MediaType = "image/png",
                StoragePath = StoragePath,
            });

            FileStore.Saved[StoragePath] = [1, 2, 3, 4];
        }

        public InMemoryKnowledgeObjectStore Store { get; } = new();

        public RecordingDocumentFileStore FileStore { get; } = new();

        public Task<ReadResourceResult> ReadAsync(string dataSourceId, string figureId)
        {
            var handler = new DataSourceFigureResourceHandler(
                Store,
                FileStore,
                NullLogger<DataSourceFigureResourceHandler>.Instance);

            var resource = new McpResource
            {
                ItemId = "figures",
                Source = DataSourceFigureResourceConstants.Type,
                Resource = new Resource
                {
                    Name = "Data source figure",
                    Uri = DataSourceFigureResourceConstants.UriTemplate,
                },
            };

            return handler.ReadAsync(
                resource,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["dataSourceId"] = dataSourceId,
                    ["figureId"] = figureId,
                },
                TestContext.Current.CancellationToken);
        }
    }
}
