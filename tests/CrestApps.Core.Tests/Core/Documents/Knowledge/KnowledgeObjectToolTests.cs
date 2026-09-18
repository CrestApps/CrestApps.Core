using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Documents.Tooling;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CrestApps.Core.Tests.Core.Documents.Knowledge;

/// <summary>
/// Covers reading one knowledge object in full by the identifier a search reported: the picture comes back
/// as a picture, a table's rows and an exactly-read chart's points come back as data, a chart read any other
/// way comes back with no numbers at all, and an identifier that names nothing is a plain answer rather than
/// an exception.
/// <para>
/// Alongside it, enumerating what a data source holds: a listing returns the kind that was asked for, stops
/// at its bound and says it stopped, walks from an object to its parent or the objects beside it, and offers
/// each figure as a marker the host can turn into a picture rather than as an address.
/// </para>
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
            ObjectType = KnowledgeObjectTypes.Table,
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
    /// Verifies that a chart whose values were lifted from the document hands its points back as data, the
    /// way a table hands back its rows, so a question about what the chart says has something to answer it.
    /// </summary>
    [Fact]
    public async Task GetSource_ExactChart_ReturnsSeriesAsJson()
    {
        var harness = new Harness();

        harness.Store.Seed(CreateChart("chart:key:1:0", ChartValueConfidence.Exact));
        harness.FileStore.Saved["figures/chart-1.png"] = [1, 2, 3, 4];

        var result = await harness.InvokeAsync("chart:key:1:0");

        var typed = Assert.IsType<KnowledgeObjectToolResult>(result);

        Assert.Contains("Values: Exact", typed.Text, StringComparison.Ordinal);
        Assert.Contains("Series (JSON):", typed.Text, StringComparison.Ordinal);

        // What it is plotted as, what it is plotted against, and the points themselves. The series carries
        // no name, so no name is written for it rather than a null standing in for one.
        Assert.Contains(
            """{"chartType":"Line","axisX":"Load in kN","axisY":"Yield in MPa","series":[{"points":[[0,1],[1,4],[2,9]]}]}""",
            typed.Text,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a chart whose values were only described hands back no number at all, and still says
    /// outright that its values are not machine-readable.
    /// </summary>
    /// <remarks>
    /// The chart is seeded with points it should never have carried, because what is under test is the
    /// confidence gate and not the emptiness of the list. Only a chart read at exact confidence ever holds
    /// points, so a branch that simply printed whatever it found would pass until one row was written wrong
    /// - and would then print an estimate in a shape indistinguishable from a measurement.
    /// </remarks>
    [Fact]
    public async Task GetSource_DescriptiveChart_EmitsNoNumbersAndSaysSo()
    {
        var harness = new Harness();

        harness.Store.Seed(CreateChart("chart:key:1:0", ChartValueConfidence.Descriptive));
        harness.FileStore.Saved["figures/chart-1.png"] = [1, 2, 3, 4];

        var result = await harness.InvokeAsync("chart:key:1:0");

        var typed = Assert.IsType<KnowledgeObjectToolResult>(result);

        Assert.Contains("Values: descriptive - not machine-readable", typed.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Series (JSON):", typed.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("[[0,1]", typed.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a chart whose axes were read but whose values were not is treated the same way, so the
    /// gate is exactness rather than merely not being descriptive.
    /// </summary>
    [Fact]
    public async Task GetSource_AxesOnlyChart_EmitsNoNumbers()
    {
        var harness = new Harness();

        harness.Store.Seed(CreateChart("chart:key:1:0", ChartValueConfidence.AxesOnly));
        harness.FileStore.Saved["figures/chart-1.png"] = [1, 2, 3, 4];

        var result = await harness.InvokeAsync("chart:key:1:0");

        var typed = Assert.IsType<KnowledgeObjectToolResult>(result);

        Assert.Contains("Values: AxesOnly", typed.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Series (JSON):", typed.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("[[0,1]", typed.Text, StringComparison.Ordinal);
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

    /// <summary>
    /// Verifies that listing a kind returns that kind and nothing else, and that asking for figures brings
    /// the charts with them, because a chart is a figure that happens to carry values.
    /// </summary>
    [Fact]
    public async Task List_FigureKind_ReturnsEveryFigureAndChartAndNothingElse()
    {
        var harness = new ListHarness();

        SeedDocument(harness.Store);

        var result = await harness.InvokeAsync(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["kind"] = KnowledgeObjectTypes.Figure,
        });

        var typed = Assert.IsType<KnowledgeObjectListToolResult>(result);

        Assert.Equal(3, typed.Entries.Count);
        Assert.All(typed.Entries, entry => Assert.True(
            entry.ObjectType is KnowledgeObjectTypes.Figure or KnowledgeObjectTypes.Chart,
            $"'{entry.Id}' is a {entry.ObjectType}, which is not a figure."));

        Assert.DoesNotContain("table:doc-1:1:3", typed.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("text:doc-1:1:0", typed.Text, StringComparison.Ordinal);
        Assert.False(typed.IsTruncated);
    }

    /// <summary>
    /// Verifies that a listing with more matches than it may return stops at the bound and says outright that
    /// what came back is not the whole set.
    /// </summary>
    /// <remarks>
    /// This is the difference between a list and a page of one. A caller asking what a document contains and
    /// handed a silent page counts it, and the count is wrong.
    /// </remarks>
    [Fact]
    public async Task List_MoreMatchesThanTheBound_StopsAtItAndSaysSoInWords()
    {
        var harness = new ListHarness();

        SeedDocument(harness.Store);

        var result = await harness.InvokeAsync(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["kind"] = KnowledgeObjectTypes.Figure,
            ["limit"] = 2,
        });

        var typed = Assert.IsType<KnowledgeObjectListToolResult>(result);

        Assert.Equal(2, typed.Entries.Count);
        Assert.True(typed.IsTruncated);
        Assert.Contains("not the whole set", typed.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that listing inside one article returns what that article contains and leaves the rest of the
    /// document out.
    /// </summary>
    [Fact]
    public async Task List_InsideOneArticle_LeavesTheOtherArticleOut()
    {
        var harness = new ListHarness();

        SeedDocument(harness.Store);

        var result = await harness.InvokeAsync(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["kind"] = KnowledgeObjectTypes.Figure,
            ["parent_id"] = "article:doc-1:1",
        });

        var typed = Assert.IsType<KnowledgeObjectListToolResult>(result);

        Assert.Equal(2, typed.Entries.Count);
        Assert.Single(typed.Entries, entry => entry.Id == "figure:doc-1:1:1");
        Assert.Single(typed.Entries, entry => entry.Id == "chart:doc-1:1:2");
        Assert.DoesNotContain("figure:doc-1:2:0", typed.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that walking up from a figure reaches the article it belongs to, so a hit on a picture can be
    /// widened to the piece it was printed in.
    /// </summary>
    [Fact]
    public async Task Walk_FromAFigure_ReachesTheArticleItBelongsTo()
    {
        var harness = new ListHarness();

        SeedDocument(harness.Store);

        var result = await harness.InvokeAsync(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["id"] = "figure:doc-1:1:1",
            ["relation"] = "parent",
        });

        var typed = Assert.IsType<KnowledgeObjectListToolResult>(result);

        var only = Assert.Single(typed.Entries);

        Assert.Equal("article:doc-1:1", only.Id);
        Assert.Equal(KnowledgeObjectTypes.Article, only.ObjectType);
    }

    /// <summary>
    /// Verifies that walking sideways from a figure reaches what sits under the same article, and nothing
    /// from the article next to it.
    /// </summary>
    [Fact]
    public async Task Walk_Siblings_ReturnsWhatSharesTheSameParent()
    {
        var harness = new ListHarness();

        SeedDocument(harness.Store);

        var result = await harness.InvokeAsync(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["id"] = "figure:doc-1:1:1",
            ["relation"] = "siblings",
            ["kind"] = KnowledgeObjectTypes.Figure,
        });

        var typed = Assert.IsType<KnowledgeObjectListToolResult>(result);

        Assert.Equal(2, typed.Entries.Count);
        Assert.Single(typed.Entries, entry => entry.Id == "figure:doc-1:1:1");
        Assert.Single(typed.Entries, entry => entry.Id == "chart:doc-1:1:2");
        Assert.DoesNotContain("figure:doc-1:2:0", typed.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a listed figure is offered the way every other figure in this codebase is: a marker
    /// registered against the host's own address, with the address itself never written where the model
    /// could copy it.
    /// </summary>
    [Fact]
    public async Task List_Figures_RegisterAMarkerTheHostCanShowAndPrintNoAddress()
    {
        var harness = new ListHarness
        {
            LinkResolver = CreateLinkResolver(),
        };

        SeedDocument(harness.Store);

        using var scope = AIInvocationScope.Begin();

        var result = await harness.InvokeAsync(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["kind"] = KnowledgeObjectTypes.Figure,
            ["parent_id"] = "article:doc-1:1",
        });

        var typed = Assert.IsType<KnowledgeObjectListToolResult>(result);

        Assert.Equal("[fig:1]", typed.Entries[0].Label);
        Assert.Contains("[fig:1]", typed.Text, StringComparison.Ordinal);

        // A model handed an address writes one of its own, near enough to look right and wrong often enough
        // to resolve to nothing, so the listing never shows one.
        Assert.DoesNotContain("/ai/knowledge/", typed.Text, StringComparison.Ordinal);

        Assert.True(scope.Context.ToolReferences.TryGetValue("[fig:1]", out var reference));
        Assert.True(reference.IsImage);
        Assert.Equal("/ai/knowledge/data-source-1/figures/figure:doc-1:1:1", reference.Link);
        Assert.Equal("figure:doc-1:1:1", reference.ReferenceId);
    }

    /// <summary>
    /// Verifies that the markers carry on from whatever this invocation already registered, so a listing run
    /// beside a retrieval never overwrites, or is overwritten by, somebody else's picture.
    /// </summary>
    [Fact]
    public async Task List_Figures_ContinueTheNumberingAlreadyUsedInThisInvocation()
    {
        var harness = new ListHarness
        {
            LinkResolver = CreateLinkResolver(),
        };

        SeedDocument(harness.Store);

        var context = new AIInvocationContext();

        context.ToolReferences["[fig:1]"] = new AICompletionReference
        {
            Link = "/already/registered",
            IsImage = true,
        };

        using var scope = AIInvocationScope.Begin(context);

        var result = await harness.InvokeAsync(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["kind"] = KnowledgeObjectTypes.Figure,
            ["parent_id"] = "article:doc-1:1",
        });

        var typed = Assert.IsType<KnowledgeObjectListToolResult>(result);

        Assert.Equal("[fig:2]", typed.Entries[0].Label);
        Assert.Equal("/already/registered", context.ToolReferences["[fig:1]"].Link);
    }

    /// <summary>
    /// Verifies that an object kept out of the index is kept out of a listing too, since enumerating it would
    /// put it back in front of a model by a route the exclusion never covered.
    /// </summary>
    [Fact]
    public async Task List_ExcludedObject_IsNeverListed()
    {
        var harness = new ListHarness();

        SeedDocument(harness.Store);

        var excluded = CreateListObject("figure:doc-1:1:9", KnowledgeObjectTypes.Figure, "document:doc-1", "article:doc-1:1", ordinal: 9, page: 4);
        excluded.Status = KnowledgeObjectStatus.Excluded;

        harness.Store.Seed(excluded);

        var result = await harness.InvokeAsync(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["kind"] = KnowledgeObjectTypes.Figure,
        });

        var typed = Assert.IsType<KnowledgeObjectListToolResult>(result);

        Assert.Equal(3, typed.Entries.Count);
        Assert.DoesNotContain("figure:doc-1:1:9", typed.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a listing which matched nothing says the store was read and holds none, rather than
    /// reading as though something had gone wrong.
    /// </summary>
    [Fact]
    public async Task List_NothingMatches_SaysTheStoreWasReadAndHoldsNone()
    {
        var harness = new ListHarness();

        harness.Store.Seed(CreateListObject("article:doc-1:1", KnowledgeObjectTypes.Article, "document:doc-1", "document:doc-1"));

        var result = await harness.InvokeAsync(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["kind"] = KnowledgeObjectTypes.Table,
        });

        var typed = Assert.IsType<KnowledgeObjectListToolResult>(result);

        Assert.Empty(typed.Entries);
        Assert.False(typed.IsTruncated);
        Assert.Contains("none. The data source was read and holds no such object.", typed.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a kind this store does not hold is answered plainly, so the model can correct itself.
    /// </summary>
    [Fact]
    public async Task List_UnknownKind_ReturnsError()
    {
        var harness = new ListHarness();

        SeedDocument(harness.Store);

        var result = await harness.InvokeAsync(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["kind"] = "diagram",
        });

        var message = Assert.IsType<string>(result);

        Assert.Contains("'diagram' is not a kind of knowledge object.", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds a link resolver that serves every figure, so what is under test is what the listing does with
    /// an address rather than whether one exists.
    /// </summary>
    /// <returns>The resolver.</returns>
    private static IAIReferenceLinkResolver CreateLinkResolver()
    {
        var resolver = new Mock<IAIReferenceLinkResolver>();

        resolver
            .Setup(instance => instance.ResolveLink(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()))
            .Returns((string referenceId, IDictionary<string, object> _) => "/ai/knowledge/data-source-1/figures/" + referenceId);

        return resolver.Object;
    }

    /// <summary>
    /// Seeds one synthetic document: two articles, the first holding a chunk of text, a figure, a chart and a
    /// table, and the second a figure and a table.
    /// </summary>
    /// <param name="store">The store to seed.</param>
    private static void SeedDocument(InMemoryKnowledgeObjectStore store)
    {
        store.Seed(CreateListObject("document:doc-1", KnowledgeObjectTypes.Document, "document:doc-1", null));
        store.Seed(CreateListObject("article:doc-1:1", KnowledgeObjectTypes.Article, "document:doc-1", "document:doc-1", ordinal: 0, page: 1));
        store.Seed(CreateListObject("text:doc-1:1:0", KnowledgeObjectTypes.Text, "document:doc-1", "article:doc-1:1", ordinal: 0, page: 1));
        store.Seed(CreateListObject("figure:doc-1:1:1", KnowledgeObjectTypes.Figure, "document:doc-1", "article:doc-1:1", ordinal: 1, page: 2));
        store.Seed(CreateListObject("chart:doc-1:1:2", KnowledgeObjectTypes.Chart, "document:doc-1", "article:doc-1:1", ordinal: 2, page: 3));
        store.Seed(CreateListObject("table:doc-1:1:3", KnowledgeObjectTypes.Table, "document:doc-1", "article:doc-1:1", ordinal: 3, page: 3));
        store.Seed(CreateListObject("article:doc-1:2", KnowledgeObjectTypes.Article, "document:doc-1", "document:doc-1", ordinal: 1, page: 5));
        store.Seed(CreateListObject("figure:doc-1:2:0", KnowledgeObjectTypes.Figure, "document:doc-1", "article:doc-1:2", ordinal: 0, page: 5));
        store.Seed(CreateListObject("table:doc-1:2:1", KnowledgeObjectTypes.Table, "document:doc-1", "article:doc-1:2", ordinal: 1, page: 6));
    }

    /// <summary>
    /// Builds one object of a document, hung off its parent the way ingestion hangs it.
    /// </summary>
    /// <param name="canonicalId">The identifier.</param>
    /// <param name="objectType">What kind of knowledge it holds.</param>
    /// <param name="root">The document it belongs to.</param>
    /// <param name="parent">The object it hangs directly off.</param>
    /// <param name="ordinal">Its position among its siblings.</param>
    /// <param name="page">The page it was read from.</param>
    /// <returns>The object.</returns>
    private static KnowledgeObject CreateListObject(
        string canonicalId,
        string objectType,
        string root,
        string parent,
        int ordinal = 0,
        int? page = null)
    {
        return new KnowledgeObject
        {
            ItemId = canonicalId,
            Source = DataSourceId,
            CanonicalId = canonicalId,
            ObjectType = objectType,
            RootId = root,
            ParentId = parent,
            Ordinal = ordinal,
            PageStart = page,
            PageEnd = page,
            Title = canonicalId,
            Content = canonicalId,
        };
    }

    private static KnowledgeObject CreateFigure(string canonicalId)
    {
        var figure = new KnowledgeObject
        {
            ItemId = canonicalId,
            Source = DataSourceId,
            CanonicalId = canonicalId,
            ObjectType = KnowledgeObjectTypes.Figure,
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
    /// Builds a chart carrying points, whatever confidence it is given.
    /// </summary>
    /// <param name="canonicalId">The identifier.</param>
    /// <param name="valueConfidence">How far the values can be trusted.</param>
    /// <returns>The chart.</returns>
    private static KnowledgeObject CreateChart(string canonicalId, string valueConfidence)
    {
        var chart = new KnowledgeObject
        {
            ItemId = canonicalId,
            Source = DataSourceId,
            CanonicalId = canonicalId,
            ObjectType = KnowledgeObjectTypes.Chart,
            Title = "Figure 3. Yield against load.",
            Content = "Figure 3. Yield against load.",
            MediaType = "image/png",
            StoragePath = "figures/chart-1.png",
            PageStart = 9,
            PageEnd = 9,
        };

        chart.Put(new ChartDetails
        {
            ChartType = ChartTypes.Line,
            ValueConfidence = valueConfidence,
            AxisX = "Load in kN",
            AxisY = "Yield in MPa",
            // Unnamed, which is what every series carries today: naming one is legend parsing, not this.
            Series = [new ChartSeries { Points = [[0d, 1d], [1d, 4d], [2d, 9d]] }],
        });

        return chart;
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

    /// <summary>
    /// Assembles the listing tool over an in-memory store. No file store: a listing names its figures and
    /// never carries their bytes.
    /// </summary>
    private sealed class ListHarness
    {
        public InMemoryKnowledgeObjectStore Store { get; } = new();

        public IAIReferenceLinkResolver LinkResolver { get; init; }

        public async Task<object> InvokeAsync(Dictionary<string, object> arguments)
        {
            var services = new ServiceCollection();

            services.AddSingleton<IKnowledgeObjectStore>(Store);

            if (LinkResolver is not null)
            {
                services.AddKeyedSingleton(AIDataSourceSourceTypes.File, LinkResolver);
            }

            services.AddLogging();

            await using var provider = services.BuildServiceProvider();

            var function = new KnowledgeObjectListToolFunction(
                "list_source",
                "Lists knowledge objects.",
                new KnowledgeObjectListToolSettings
                {
                    DataSourceId = DataSourceId,
                });

            return await function.InvokeAsync(
                new AIFunctionArguments(arguments)
                {
                    Services = provider,
                },
                TestContext.Current.CancellationToken);
        }
    }
}
