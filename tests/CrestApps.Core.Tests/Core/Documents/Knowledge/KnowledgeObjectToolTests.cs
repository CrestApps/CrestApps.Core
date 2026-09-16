using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Documents;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Documents.Tooling;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Knowledge;

/// <summary>
/// Covers reading one knowledge object in full by the identifier a search reported: the picture comes back
/// as a picture, a table's rows come back as data, and an identifier that names nothing is a plain answer
/// rather than an exception.
/// </summary>
public sealed class KnowledgeObjectToolTests
{
    private const string DataSourceId = "data-source-1";

    /// <summary>
    /// Verifies that asking for a figure returns both what can be said about it and the picture itself.
    /// </summary>
    [Fact]
    public async Task GetSource_FigureId_ReturnsTextAndImage()
    {
        var harness = new Harness();

        harness.Store.Seed(CreateFigure("figure:key:1:0"));
        harness.FileStore.Saved["figures/figure-1.png"] = [1, 2, 3, 4];

        var result = await harness.InvokeAsync("figure:key:1:0");

        var typed = Assert.IsType<KnowledgeObjectToolResult>(result);

        Assert.Contains("Figure 1. The measurements.", typed.Text, StringComparison.Ordinal);
        Assert.Contains("Page: 8", typed.Text, StringComparison.Ordinal);

        var contents = typed.ToContents();

        Assert.Equal(2, contents.Count);
        Assert.IsType<TextContent>(contents[0]);

        var data = Assert.IsType<DataContent>(contents[1]);

        Assert.Equal("image/png", data.MediaType);
        Assert.Equal([1, 2, 3, 4], data.Data.ToArray());
    }

    /// <summary>
    /// Verifies that when the host serves figures, the text names the address a person can open, so a chat
    /// answer can show the picture as a markdown image.
    /// </summary>
    [Fact]
    public async Task GetSource_FigureWithHostLink_NamesTheImageAddress()
    {
        var resolver = new Mock<IAIReferenceLinkResolver>();
        resolver
            .Setup(instance => instance.ResolveLink("figure:key:1:0", It.IsAny<IDictionary<string, object>>()))
            .Returns("/ai/knowledge/data-source-1/figures/figure:key:1:0");

        var harness = new Harness
        {
            LinkResolver = resolver.Object,
        };

        harness.Store.Seed(CreateFigure("figure:key:1:0"));
        harness.FileStore.Saved["figures/figure-1.png"] = [1, 2, 3, 4];

        var result = await harness.InvokeAsync("figure:key:1:0");

        var typed = Assert.IsType<KnowledgeObjectToolResult>(result);

        Assert.Contains("Image: /ai/knowledge/data-source-1/figures/figure:key:1:0", typed.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a picture too large to send inline comes back as a link rather than not at all.
    /// </summary>
    [Fact]
    public async Task GetSource_OversizeFigure_ReturnsLinkInsteadOfBytes()
    {
        var harness = new Harness
        {
            MaxVisionImageBytesPerFile = 2,
        };

        harness.Store.Seed(CreateFigure("figure:key:1:0"));
        harness.FileStore.Saved["figures/figure-1.png"] = [1, 2, 3, 4];

        var result = await harness.InvokeAsync("figure:key:1:0");

        var typed = Assert.IsType<KnowledgeObjectToolResult>(result);

        Assert.True(typed.Content.IsEmpty);

        var contents = typed.ToContents();

        Assert.Equal(2, contents.Count);

        var link = Assert.IsType<UriContent>(contents[1]);

        Assert.Equal("crestapps://datasource/data-source-1/figure/figure:key:1:0", link.Uri.ToString());
    }

    /// <summary>
    /// Verifies that a table hands back its rows as data, so a caller that wants to compute with them does
    /// not have to parse a sentence back into numbers.
    /// </summary>
    [Fact]
    public async Task GetSource_TableId_ReturnsRowsAsJson()
    {
        var harness = new Harness();

        var table = new KnowledgeObject
        {
            ItemId = "table:key:1:0",
            Source = DataSourceId,
            CanonicalId = "table:key:1:0",
            ObjectType = KnowledgeContentTypes.Table,
            Title = "Table 2.",
            Content = "Table 2.",
            PageStart = 5,
            PageEnd = 5,
        };

        table.Put(new TableDetails
        {
            Caption = "Table 2.",
            Columns = ["Material", "Strength"],
            Rows = [["Steel", "400"]],
        });

        harness.Store.Seed(table);

        var result = await harness.InvokeAsync("table:key:1:0");

        var typed = Assert.IsType<KnowledgeObjectToolResult>(result);

        Assert.Contains("Rows (JSON):", typed.Text, StringComparison.Ordinal);
        Assert.Contains("\"Steel\"", typed.Text, StringComparison.Ordinal);
        Assert.True(typed.Content.IsEmpty);
    }

    /// <summary>
    /// Verifies that an identifier that is not a knowledge object identifier is answered plainly, so the
    /// model can correct itself instead of diagnosing an error.
    /// </summary>
    [Fact]
    public async Task GetSource_UnknownPrefix_ReturnsError()
    {
        var harness = new Harness();

        var result = await harness.InvokeAsync("doc-1");

        var message = Assert.IsType<string>(result);

        Assert.Contains("is not a knowledge object identifier", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that an object belonging to another data source reads as not found, because the identifier
    /// alone says nothing about which knowledge base it came from.
    /// </summary>
    [Fact]
    public async Task GetSource_ObjectFromAnotherDataSource_ReadsAsNotFound()
    {
        var harness = new Harness();

        var figure = CreateFigure("figure:key:1:0");
        figure.Source = "data-source-2";

        harness.Store.Seed(figure);

        var result = await harness.InvokeAsync("figure:key:1:0");

        var message = Assert.IsType<string>(result);

        Assert.Contains("No object with identifier 'figure:key:1:0' was found in this data source.", message, StringComparison.Ordinal);
    }

    private static KnowledgeObject CreateFigure(string canonicalId)
    {
        var figure = new KnowledgeObject
        {
            ItemId = canonicalId,
            Source = DataSourceId,
            CanonicalId = canonicalId,
            ObjectType = KnowledgeContentTypes.Figure,
            Title = "Figure 1. The measurements.",
            Content = "Figure 1. The measurements.",
            MediaType = "image/png",
            StoragePath = "figures/figure-1.png",
            PageStart = 8,
            PageEnd = 8,
        };

        figure.Put(new FigureDetails
        {
            Caption = "Figure 1. The measurements.",
        });

        return figure;
    }

    /// <summary>
    /// Assembles the tool over an in-memory store and file store.
    /// </summary>
    private sealed class Harness
    {
        public InMemoryKnowledgeObjectStore Store { get; } = new();

        public RecordingDocumentFileStore FileStore { get; } = new();

        public long MaxVisionImageBytesPerFile { get; init; }

        public IAIReferenceLinkResolver LinkResolver { get; init; }

        public async Task<object> InvokeAsync(string id)
        {
            var services = new ServiceCollection();

            services.AddSingleton<IKnowledgeObjectStore>(Store);
            services.AddSingleton<IDocumentFileStore>(FileStore);

            if (LinkResolver is not null)
            {
                services.AddKeyedSingleton(AIDataSourceSourceTypes.File, LinkResolver);
            }
            services.Configure<ChatDocumentsOptions>(options =>
            {
                if (MaxVisionImageBytesPerFile > 0)
                {
                    options.MaxVisionImageBytesPerFile = MaxVisionImageBytesPerFile;
                }
            });
            services.AddLogging();

            await using var provider = services.BuildServiceProvider();

            var function = new KnowledgeObjectToolFunction(
                "get_source",
                "Reads one knowledge object.",
                new KnowledgeObjectToolSettings
                {
                    DataSourceId = DataSourceId,
                });

            return await function.InvokeAsync(
                new AIFunctionArguments(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["id"] = id,
                })
                {
                    Services = provider,
                },
                TestContext.Current.CancellationToken);
        }
    }
}
