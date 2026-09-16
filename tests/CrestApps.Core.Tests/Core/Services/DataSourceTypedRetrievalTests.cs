using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Azure.AISearch.Services;
using CrestApps.Core.Elasticsearch;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.DataSources;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.PostgreSQL;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Services;

/// <summary>
/// Covers what typed knowledge added to retrieval: figures and tables named as objects rather than buried in
/// prose, a chart that offers its points when they came from the document and says outright when they were
/// read by eye, a filter that narrows a search to one kind of knowledge, and hits from one document rendered
/// together.
/// </summary>
public sealed class DataSourceTypedRetrievalTests
{
    private const string DataSourceId = "data-source-1";
    private const string ProviderName = "TestProvider";
    private const string KnowledgeBaseName = "kb-index";
    private const string EmbeddingDeploymentName = "text-embedding-3-small";

    // Deliberately unlike anything the block prints, so "the address never reaches the model" is a claim a
    // test can actually check.
    private const string FigureLink = "https://host.example/knowledge/figures/a1";
    private const string SecondFigureLink = "https://host.example/knowledge/figures/b2";

    /// <summary>
    /// Verifies that a figure among the hits is named in its own block with its label and page, and comes
    /// back as an object a caller can act on. No address is printed: the label is what the model is given.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_FigureHit_RendersFiguresBlockAndPopulatesFigures()
    {
        var harness = new Harness(
        [
            Text("text:key:1:0", "report.pdf", "The rig was measured at three loads.", page: 8),
            Figure("figure:key:1:0", "Figure 1. The measurements.", page: 8),
        ]);

        var result = await harness.SearchAsync();

        Assert.Contains("Figures:", result.Text, StringComparison.Ordinal);
        Assert.Contains("[fig:1] Figure 1. The measurements. (p. 8)", result.Text, StringComparison.Ordinal);

        // The logical address is for an MCP client to read the picture through. It is never printed into the
        // text the model reads, because an address a model has seen is an address it will try to rewrite.
        Assert.DoesNotContain("crestapps://", result.Text, StringComparison.Ordinal);

        var figure = Assert.Single(result.Figures);

        Assert.Equal($"crestapps://datasource/{DataSourceId}/figure/figure:key:1:0", figure.Uri);

        Assert.Equal("figure:key:1:0", figure.Id);
        Assert.Equal("[fig:1]", figure.Label);
        Assert.Equal("Figure 1. The measurements.", figure.Caption);
        Assert.Equal("image/png", figure.MediaType);
        Assert.Equal(8, figure.Page);

        Assert.Empty(result.Tables);
    }

    /// <summary>
    /// Verifies that a chart whose values were only described says so, because a number read off a picture by
    /// eye looks exactly like one lifted from the file's own geometry.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_DescriptiveChart_StatesValuesNotMachineReadable()
    {
        var chart = Figure("chart:key:1:0", "Figure 2. Yield by material.", page: 5);
        chart.ContentType = KnowledgeContentTypes.Chart;
        chart.Filters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["caption"] = "Figure 2. Yield by material.",
            ["mediaType"] = "image/png",
            ["valueConfidence"] = ChartValueConfidence.Descriptive,
        };

        var harness = new Harness([chart]);

        var result = await harness.SearchAsync();

        Assert.Contains("values: descriptive - not machine-readable", result.Text, StringComparison.Ordinal);
        Assert.Equal(ChartValueConfidence.Descriptive, Assert.Single(result.Figures).ValueConfidence);
    }

    /// <summary>
    /// Verifies that a chart whose values were lifted from the document offers them, so a chart among the
    /// hits is something the model can answer about rather than only describe.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_ExactChart_OffersItsPoints()
    {
        var chart = Chart(
            "chart:key:1:0",
            "Figure 3. Yield against load.",
            page: 6,
            ChartValueConfidence.Exact,
            """[{"points":[[0,1],[1,4],[2,9]]}]""");

        var harness = new Harness([chart]);

        var result = await harness.SearchAsync();

        Assert.Contains("values: Exact", result.Text, StringComparison.Ordinal);

        // The series carries no name, so none is written for it: naming it is legend parsing, not this.
        Assert.Contains("""series (JSON): [{"points":[[0,1],[1,4],[2,9]]}]""", result.Text, StringComparison.Ordinal);

        // Nothing was left out, so nothing says anything was.
        Assert.DoesNotContain("truncated", result.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a chart whose values were only described offers no number, whatever its row happens to
    /// store, and still says outright that its values are not machine-readable.
    /// </summary>
    /// <remarks>
    /// The row is given points it should never have carried. Only a chart read at exact confidence holds
    /// any, so a block that printed whatever the row stored would pass every ordinary test and then, on one
    /// row written wrong, hand the model an estimate in a shape indistinguishable from a measurement.
    /// </remarks>
    [Fact]
    public async Task SearchDetailed_DescriptiveChartWithStoredPoints_OffersNoNumbers()
    {
        var chart = Chart(
            "chart:key:1:0",
            "Figure 3. Yield against load.",
            page: 6,
            ChartValueConfidence.Descriptive,
            """[{"points":[[0,1],[1,4],[2,9]]}]""");

        var harness = new Harness([chart]);

        var result = await harness.SearchAsync();

        Assert.Contains("values: descriptive - not machine-readable", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("series (JSON)", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("[[0,1]", result.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a series too long to inline is cut to the cap and says so, rather than being quietly
    /// shortened into a chart the model would read as complete.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_LongExactSeries_SaysWhatWasLeftOut()
    {
        var chart = Chart(
            "chart:key:1:0",
            "Figure 4. Load over time.",
            page: 7,
            ChartValueConfidence.Exact,
            SeriesJson(seriesCount: 1, pointsPerSeries: 30));

        var harness = new Harness([chart]);

        var result = await harness.SearchAsync();

        Assert.Contains("truncated: showing the first 24 of 30 points.", result.Text, StringComparison.Ordinal);

        // The last point inlined, and the first one left out, so the cap is the one that was announced.
        Assert.Contains("[23,46]", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("[24,48]", result.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a chart carrying more series than are inlined counts the ones it left out, so the
    /// points shown are never read as every series the chart plots.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_ManyExactSeries_SaysHowManyWereLeftOut()
    {
        var chart = Chart(
            "chart:key:1:0",
            "Figure 5. Load over time, by rig.",
            page: 8,
            ChartValueConfidence.Exact,
            SeriesJson(seriesCount: 6, pointsPerSeries: 2));

        var harness = new Harness([chart]);

        var result = await harness.SearchAsync();

        Assert.Contains("truncated: showing the first 8 of 12 points from 4 of 6 series.", result.Text, StringComparison.Ordinal);

        // The fourth series is inlined and the fifth is not.
        Assert.Contains("[0,300]", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("[0,400]", result.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a table among the hits is named with the columns it holds, so the model can tell whether
    /// it is worth asking about before anything reads its rows.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_TableHit_RendersColumns()
    {
        var table = new DataSourceSearchResult
        {
            ReferenceId = "table:key:1:0",
            Title = "Table 2. Mechanical properties.",
            Content = "Table 2. Mechanical properties.\nMaterial | Strength",
            ContentType = KnowledgeContentTypes.Table,
            RootId = "document:key",
            ParentId = "article:key:1",
            Page = 5,
            Score = 0.8f,
            DataSourceId = DataSourceId,
            Filters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["caption"] = "Table 2. Mechanical properties.",
                ["columns"] = "Material, Strength",
            },
        };

        var harness = new Harness([table]);

        var result = await harness.SearchAsync();

        Assert.Contains("Tables:", result.Text, StringComparison.Ordinal);
        Assert.Contains("[tbl:1] Table 2. Mechanical properties. - columns: Material, Strength (p. 5)", result.Text, StringComparison.Ordinal);
        Assert.Equal("Material, Strength", Assert.Single(result.Tables).Columns);
    }

    /// <summary>
    /// Verifies that asking for one kind of knowledge reaches the provider as a filter, and that asking for
    /// text also asks for rows written before the column existed.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_ContentTypesFilter_IsTranslated()
    {
        var harness = new Harness([Figure("figure:key:1:0", "Figure 1.", page: 1)]);

        await harness.SearchAsync(contentTypes: ["figure", "chart"]);

        Assert.Equal("translated:(contentType eq 'figure' or contentType eq 'chart')", harness.LastFilter);

        await harness.SearchAsync(contentTypes: ["text"]);

        Assert.Equal("translated:(contentType eq 'text' or contentType eq null)", harness.LastFilter);

        await harness.SearchAsync(filter: "language eq 'hu'", contentTypes: ["figure"]);

        Assert.Equal("translated:(language eq 'hu') and contentType eq 'figure'", harness.LastFilter);
    }

    /// <summary>
    /// Verifies that an index which cannot filter by content type still answers the search, and says so.
    /// </summary>
    /// <remarks>
    /// An index built before the typed columns existed rejects a filter that names one, and every provider
    /// reports that by logging and handing back nothing rather than by throwing. Read as "no matches", a
    /// whole search fails closed over a narrowing the caller could have done without — while the tool
    /// advertises the parameter to the model unconditionally, so the model reaches this on its own.
    /// </remarks>
    [Fact]
    public async Task SearchDetailed_IndexCannotFilterByContentType_ReturnsRowsAndSaysItDidNotNarrow()
    {
        var text = Text("text:key:1:0", "report.pdf", "The rig was measured at three loads.", page: 8);

        var harness = new Harness([])
        {
            ResultsForFilter = providerFilter => providerFilter is not null && providerFilter.Contains("contentType", StringComparison.Ordinal)
                ? []
                : [text],
        };

        var result = await harness.SearchAsync(contentTypes: ["figure", "chart"]);

        Assert.Equal(2, harness.Filters.Count);
        Assert.Equal("translated:(contentType eq 'figure' or contentType eq 'chart')", harness.Filters[0]);

        // The second attempt drops the narrowing entirely rather than returning nothing.
        Assert.Null(harness.Filters[1]);

        Assert.Contains("The rig was measured at three loads.", result.Text, StringComparison.Ordinal);
        Assert.Contains("cannot filter by content type", result.Text, StringComparison.Ordinal);
        Assert.Contains("not narrowed to figure, chart", result.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a search which genuinely matches nothing still reports nothing, rather than claiming a
    /// narrowing was dropped when the index honored it perfectly well.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_NothingMatchesAtAll_DoesNotClaimTheFilterWasDropped()
    {
        var harness = new Harness([])
        {
            ResultsForFilter = _ => [],
        };

        var result = await harness.SearchAsync(contentTypes: ["figure"]);

        Assert.DoesNotContain("cannot filter by content type", result.Text, StringComparison.Ordinal);
        Assert.Contains("No relevant content was found", result.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a data source which stores no typed knowledge keeps its own field of a reserved name,
    /// and that a value is never mistaken for a field.
    /// </summary>
    /// <remarks>
    /// The typed columns belong to ingested knowledge. A data source whose documents have carried a
    /// top-level <c>contentType</c> of their own since long before those columns existed would otherwise
    /// have every filter on it translated into a lookup against a column its rows never fill.
    /// </remarks>
    [Fact]
    public async Task SearchDetailed_DataSourceWithoutTypedKnowledge_KeepsItsOwnContentTypeField()
    {
        var harness = new Harness([Text("text:key:1:0", "report.pdf", "The body.", page: 1)])
        {
            SourceType = AIDataSourceSourceTypes.SearchIndexProfile,
        };

        await harness.SearchAsync(filter: "contentType eq 'news'");

        Assert.Equal("translated:filters.contentType eq 'news'", harness.LastFilter);

        await harness.SearchAsync(filter: "category eq 'contentType'");

        Assert.Equal("translated:category eq 'contentType'", harness.LastFilter);
    }

    /// <summary>
    /// Verifies that a data source which does store typed knowledge still filters on the column itself, so
    /// scoping the reserved names took nothing away from the knowledge base.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_IngestedDataSource_StillFiltersOnTheTypedColumn()
    {
        var harness = new Harness([Text("text:key:1:0", "report.pdf", "The body.", page: 1)]);

        await harness.SearchAsync(filter: "contentType eq 'figure'");

        Assert.Equal("translated:contentType eq 'figure'", harness.LastFilter);
    }

    /// <summary>
    /// Verifies that hits from one document render together even when a higher-scoring hit from another
    /// document sits between them, so a figure and the paragraph that cites it are read as related.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_GroupsByRootThenParent()
    {
        var first = Text("text:a:1:0", "First report", "The first body.", page: 1);
        first.Score = 0.95f;

        var other = Text("text:b:1:0", "Second report", "The other body.", page: 1);
        other.RootId = "document:b";
        other.ParentId = "article:b:1";
        other.Score = 0.9f;

        var firstFigure = Figure("figure:a:1:0", "Figure 1. The first figure.", page: 2);
        firstFigure.Score = 0.85f;

        var harness = new Harness([first, other, firstFigure]);

        var result = await harness.SearchAsync();

        var firstIndex = result.Text.IndexOf("The first body.", StringComparison.Ordinal);
        var figureIndex = result.Text.IndexOf("Figure 1. The first figure.", StringComparison.Ordinal);
        var otherIndex = result.Text.IndexOf("The other body.", StringComparison.Ordinal);

        Assert.True(firstIndex < figureIndex, "The first document's own hits must render together.");
        Assert.True(figureIndex < otherIndex, "The second document must render after the first is finished.");
    }

    /// <summary>
    /// Verifies that a figure is cited under the document it came from and the page it was printed on, not
    /// under its own caption.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_FigureCitation_NamesTheDocumentAndPage()
    {
        var harness = new Harness(
        [
            Text("text:key:1:0", "report.pdf", "The rig was measured.", page: 7),
            Figure("figure:key:1:0", "Figure 1. The measurements.", page: 8),
        ]);

        var result = await harness.SearchAsync();

        Assert.Contains("Title: report.pdf, p. 8", result.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a figure the host can serve is rendered under its label and that its address appears
    /// nowhere in the text handed to the model, which is told to write the label and never a URL.
    /// </summary>
    /// <remarks>
    /// This is the regression. Retrieval used to print each figure's address and ask the model to embed it
    /// as a markdown image. A model does not copy a long opaque identifier — it copies its shape and
    /// substitutes its own ordinals, so answers pointed at figures that were never among the results, with
    /// addresses that looked exactly like working ones. An address the model never sees is one it cannot
    /// rewrite.
    /// </remarks>
    [Fact]
    public async Task SearchDetailed_FigureWithHostLink_RendersTheLabelAndNeverItsAddress()
    {
        var figure = Figure("figure:key:1:0", "Figure 1. The measurements.", page: 8);

        figure.ReferenceType = AIDataSourceSourceTypes.File;

        var harness = new Harness([figure])
        {
            LinkResolver = Resolver(("figure:key:1:0", FigureLink)),
        };

        var result = await harness.SearchAsync();

        var retrieved = Assert.Single(result.Figures);

        Assert.Equal(FigureLink, retrieved.Link);
        Assert.Contains("[fig:1] Figure 1. The measurements. (p. 8)", result.Text, StringComparison.Ordinal);

        // Not the host's address, not the logical one, and nothing else shaped like an address either.
        Assert.DoesNotContain(FigureLink, result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("crestapps://", result.Text, StringComparison.Ordinal);

        // What the model is told to do instead, and what it is told never to do.
        Assert.Contains("write its label exactly as printed", result.Text, StringComparison.Ordinal);
        Assert.Contains("Never write a URL for a figure", result.Text, StringComparison.Ordinal);

        // The logical address is still what an MCP client reads the picture through.
        Assert.Equal($"crestapps://datasource/{DataSourceId}/figure/figure:key:1:0", retrieved.Uri);
    }

    /// <summary>
    /// Verifies that a figure the host can serve is registered on the invocation context under exactly the
    /// label that was rendered into the text, carrying the picture's address and its alt text.
    /// </summary>
    /// <remarks>
    /// The label and the key are read back from the result rather than written out here twice, so a change
    /// to the label format cannot leave the text saying one thing and the reference map keyed by another.
    /// </remarks>
    [Fact]
    public async Task SearchDetailed_FigureWithHostLink_RegistersTheLabelAsAnImageReference()
    {
        var figure = Figure("figure:key:1:0", "Figure 1. The measurements.", page: 8);

        figure.ReferenceType = AIDataSourceSourceTypes.File;

        var harness = new Harness([figure])
        {
            LinkResolver = Resolver(("figure:key:1:0", FigureLink)),
        };

        var result = await harness.SearchAsync();

        var retrieved = Assert.Single(result.Figures);
        var reference = Assert.Single(harness.References, pair => pair.Value.IsImage);

        // The key is the marker itself, byte for byte the label the block printed.
        Assert.Equal(retrieved.Label, reference.Key);
        Assert.Contains(reference.Key, result.Text, StringComparison.Ordinal);

        Assert.True(reference.Value.IsImage);
        Assert.Equal(FigureLink, reference.Value.Link);
        Assert.Equal("Figure 1. The measurements.", reference.Value.Title);
        Assert.Equal(1, reference.Value.Index);
        Assert.Equal("figure:key:1:0", reference.Value.ReferenceId);
        Assert.Equal(AIDataSourceSourceTypes.File, reference.Value.ReferenceType);
        Assert.Equal(DataSourceId, reference.Value.DataSourceId);
    }

    /// <summary>
    /// Verifies that two figures among the hits get sequential labels and two references of their own, each
    /// keyed by its own label and carrying its own picture.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_TwoFigures_GetSequentialLabelsAndOneReferenceEach()
    {
        var first = Figure("figure:key:1:0", "Figure 1. The first measurement.", page: 8);
        var second = Figure("figure:key:1:1", "Figure 2. The second measurement.", page: 9);

        first.ReferenceType = AIDataSourceSourceTypes.File;
        second.ReferenceType = AIDataSourceSourceTypes.File;
        second.Score = 0.7f;

        var harness = new Harness([first, second])
        {
            LinkResolver = Resolver(("figure:key:1:0", FigureLink), ("figure:key:1:1", SecondFigureLink)),
        };

        var result = await harness.SearchAsync();

        Assert.Equal(2, result.Figures.Count);
        Assert.Contains(result.Figures, item => item.Label == "[fig:1]");
        Assert.Contains(result.Figures, item => item.Label == "[fig:2]");

        var images = harness.References.Where(pair => pair.Value.IsImage).ToList();

        Assert.Equal(2, images.Count);

        foreach (var retrieved in result.Figures)
        {
            var reference = harness.References[retrieved.Label];

            Assert.True(reference.IsImage);
            Assert.Equal(retrieved.Link, reference.Link);
            Assert.Equal(retrieved.Title, reference.Title);
            Assert.Contains(retrieved.Label, result.Text, StringComparison.Ordinal);
        }

        Assert.NotEqual(images[0].Value.Link, images[1].Value.Link);
    }

    /// <summary>
    /// Verifies that a figure whose row comes back without the typed column is still recognized as a
    /// figure, still gets its link, and is still registered under its label.
    /// </summary>
    /// <remarks>
    /// An index built before the typed columns existed, one whose provider drops fields it has no mapping
    /// for, or one whose schema upgrade could not run, all return a figure row that says it is text. Read
    /// that way it keeps its caption and loses its picture. The identifier states the kind and travels with
    /// the row, so it stands in.
    /// </remarks>
    [Fact]
    public async Task SearchDetailed_FigureRowMissingItsContentType_IsStillAFigure()
    {
        var figure = Figure("figure:key:1:0", "Figure 1. The measurements.", page: 8);

        figure.ReferenceType = AIDataSourceSourceTypes.File;

        // What an index that never stored the column hands back.
        figure.ContentType = KnowledgeContentTypes.Text;

        var harness = new Harness([figure])
        {
            LinkResolver = Resolver(("figure:key:1:0", FigureLink)),
        };

        var result = await harness.SearchAsync();

        var retrieved = Assert.Single(result.Figures);

        Assert.Equal(FigureLink, retrieved.Link);
        Assert.Contains("Figures:", result.Text, StringComparison.Ordinal);
        Assert.Equal(retrieved.Label, Assert.Single(harness.References, pair => pair.Value.IsImage).Key);
    }

    /// <summary>
    /// Verifies that a figure with no servable address is never offered as a picture: it stays listed, the
    /// model is told not to invent an address for it, and nothing is registered as an image.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_FiguresWithNoLink_TellTheModelNotToInventOne()
    {
        var figure = Figure("figure:key:1:0", "Figure 1. The measurements.", page: 8);

        // No resolver is registered for this reference type, so no address can be built.
        figure.ReferenceType = "SomethingElse";

        var harness = new Harness([figure]);

        var result = await harness.SearchAsync();

        Assert.Null(Assert.Single(result.Figures).Link);
        Assert.Contains("[fig:1] Figure 1. The measurements. (p. 8)", result.Text, StringComparison.Ordinal);
        Assert.Contains("never invent a URL", result.Text, StringComparison.Ordinal);

        // A label the host cannot turn into a picture is never handed to the model as one.
        Assert.DoesNotContain("write its label exactly as printed", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.References, pair => pair.Value.IsImage);
    }

    /// <summary>
    /// Verifies that when only some of the figures can be shown, the ones that cannot are named, so the
    /// instruction to write a label is not read as covering every figure listed.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_OnlySomeFiguresHaveLinks_NamesTheOnesThatCannotBeShown()
    {
        var servable = Figure("figure:key:1:0", "Figure 1. The measurements.", page: 8);
        var unservable = Figure("figure:key:1:1", "Figure 2. The apparatus.", page: 9);

        servable.ReferenceType = AIDataSourceSourceTypes.File;

        // No resolver is registered for this reference type, so this one has no address.
        unservable.ReferenceType = "SomethingElse";
        unservable.Score = 0.7f;

        var harness = new Harness([servable, unservable])
        {
            LinkResolver = Resolver(("figure:key:1:0", FigureLink)),
        };

        var result = await harness.SearchAsync();

        var shown = Assert.Single(result.Figures, item => item.Link is not null);
        var described = Assert.Single(result.Figures, item => item.Link is null);

        Assert.Contains("write its label exactly as printed", result.Text, StringComparison.Ordinal);
        Assert.Contains($"cannot be shown: {described.Label}", result.Text, StringComparison.Ordinal);

        var reference = Assert.Single(harness.References, pair => pair.Value.IsImage);

        Assert.Equal(shown.Label, reference.Key);
    }

    /// <summary>
    /// Verifies that text citations still register under their own markers, so nothing about figures
    /// changed how a document is cited.
    /// </summary>
    [Fact]
    public async Task SearchDetailed_TextHit_StillCitesTheDocumentUnderItsOwnMarker()
    {
        var harness = new Harness([Text("text:key:1:0", "report.pdf", "The rig was measured at three loads.", page: 8)]);

        var result = await harness.SearchAsync();

        Assert.Contains("[doc:1] Title: report.pdf, p. 8", result.Text, StringComparison.Ordinal);
        Assert.Contains("References:", result.Text, StringComparison.Ordinal);

        var reference = Assert.Single(harness.References);

        Assert.Equal("[doc:1]", reference.Key);
        Assert.False(reference.Value.IsImage);
        Assert.Equal("report.pdf, p. 8", reference.Value.Title);
        Assert.Equal("text:key:1:0", reference.Value.ReferenceId);
    }

    /// <summary>
    /// Builds a link resolver that answers for the named figures and for nothing else, which is how a figure
    /// the host cannot serve is arranged.
    /// </summary>
    /// <param name="links">Each figure identifier and the address the host serves its picture from.</param>
    /// <returns>The resolver.</returns>
    private static IAIReferenceLinkResolver Resolver(params (string ReferenceId, string Link)[] links)
    {
        var resolver = new Mock<IAIReferenceLinkResolver>();

        foreach (var (referenceId, link) in links)
        {
            resolver
                .Setup(instance => instance.ResolveLink(referenceId, It.IsAny<IDictionary<string, object>>()))
                .Returns(link);
        }

        return resolver.Object;
    }

    private static DataSourceSearchResult Text(string referenceId, string title, string content, int page)
    {
        return new DataSourceSearchResult
        {
            ReferenceId = referenceId,
            Title = title,
            Content = content,
            ContentType = KnowledgeContentTypes.Text,
            RootId = "document:key",
            ParentId = "article:key:1",
            Page = page,
            Score = 0.9f,
            DataSourceId = DataSourceId,
        };
    }

    private static DataSourceSearchResult Figure(string referenceId, string caption, int page)
    {
        return new DataSourceSearchResult
        {
            ReferenceId = referenceId,
            Title = caption,
            Content = caption,
            ContentType = KnowledgeContentTypes.Figure,
            RootId = "document:key",
            ParentId = "article:key:1",
            Page = page,
            Score = 0.8f,
            DataSourceId = DataSourceId,
            Filters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["caption"] = caption,
                ["mediaType"] = "image/png",
            },
        };
    }

    /// <summary>
    /// Builds a chart row as the index hands one back.
    /// </summary>
    /// <param name="referenceId">The identifier.</param>
    /// <param name="caption">The caption.</param>
    /// <param name="page">The page it was printed on.</param>
    /// <param name="valueConfidence">How far its values can be trusted.</param>
    /// <param name="series">The points stored with the row, or <see langword="null"/> when it stored none.</param>
    /// <returns>The row.</returns>
    private static DataSourceSearchResult Chart(string referenceId, string caption, int page, string valueConfidence, string series)
    {
        var chart = Figure(referenceId, caption, page);

        chart.ContentType = KnowledgeContentTypes.Chart;

        var filters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["caption"] = caption,
            ["mediaType"] = "image/png",
            ["valueConfidence"] = valueConfidence,
        };

        if (series is not null)
        {
            filters["series"] = series;
        }

        chart.Filters = filters;

        return chart;
    }

    /// <summary>
    /// Writes the points a chart row stores, in the shape the index hands them back in.
    /// </summary>
    /// <param name="seriesCount">How many series the chart plots.</param>
    /// <param name="pointsPerSeries">How many points each one carries.</param>
    /// <returns>The stored value.</returns>
    /// <remarks>
    /// Each series is offset by a hundred so a test can name one point and say which series it belongs to.
    /// </remarks>
    private static string SeriesJson(int seriesCount, int pointsPerSeries)
    {
        var series = Enumerable.Range(0, seriesCount).Select(seriesIndex =>
        {
            var points = Enumerable.Range(0, pointsPerSeries)
                .Select(index => "[" + index + "," + ((seriesIndex * 100) + (index * 2)) + "]");

            return "{\"points\":[" + string.Join(",", points) + "]}";
        });

        return "[" + string.Join(",", series) + "]";
    }

    /// <summary>
    /// Assembles the services a search resolves, and records the filter the provider was handed.
    /// </summary>
    private sealed class Harness
    {
        private readonly IReadOnlyList<DataSourceSearchResult> _results;
        private readonly List<string> _filters = [];

        public Harness(IReadOnlyList<DataSourceSearchResult> results)
        {
            _results = results;
        }

        public string LastFilter { get; private set; }

        /// <summary>
        /// Gets every filter the provider was handed, in the order it was handed them.
        /// </summary>
        public List<string> Filters => _filters;

        /// <summary>
        /// Gets the references the last search registered on the invocation context, keyed by the marker the
        /// model is told to write.
        /// </summary>
        public Dictionary<string, AICompletionReference> References { get; private set; } = [];

        public IAIReferenceLinkResolver LinkResolver { get; init; }

        /// <summary>
        /// Gets the kind of data source being searched. Only an ingested one stores typed knowledge, so only
        /// its rows carry the typed columns.
        /// </summary>
        public string SourceType { get; init; } = AIDataSourceSourceTypes.File;

        /// <summary>
        /// Gets what the index returns for a given provider filter, for a test that needs the index to
        /// behave differently depending on what it was asked.
        /// </summary>
        public Func<string, IReadOnlyList<DataSourceSearchResult>> ResultsForFilter { get; init; }

        public async Task<DataSourceRetrievalResult> SearchAsync(string filter = null, string[] contentTypes = null)
        {
            await using var services = BuildServices();

            // Retrieval registers every marker it renders on the ambient invocation context, which is how the
            // host turns "[doc:1]" into a footnote and "[fig:1]" into the picture. Without a scope there is
            // nothing to register on, so each search a test runs gets one of its own.
            using var scope = AIInvocationScope.Begin();

            var result = await DataSourceRetrieval.SearchDetailedAsync(
                services,
                new DataSourceRetrievalRequest
                {
                    DataSourceId = DataSourceId,
                    Queries = ["the measurements"],
                    RetrievalMode = DataSourceRetrievalMode.Chunk,
                    Filter = filter,
                    ContentTypes = contentTypes,
                },
                "knowledge-base",
                NullLogger.Instance,
                TestContext.Current.CancellationToken);

            References = scope.Context.ToolReferences;

            return result;
        }

        private ServiceProvider BuildServices()
        {
            var dataSource = new AIDataSource
            {
                ItemId = DataSourceId,
                Source = SourceType,
                DisplayText = "Knowledge base",
                AIKnowledgeBaseIndexProfileName = KnowledgeBaseName,
            };

            var indexProfile = new SearchIndexProfile
            {
                Name = KnowledgeBaseName,
                ProviderName = ProviderName,
                Type = IndexProfileTypes.DataSource,
                EmbeddingDeploymentName = EmbeddingDeploymentName,
            };

            var dataSourceStore = new Mock<IAIDataSourceStore>();
            dataSourceStore
                .Setup(store => store.FindByIdAsync(DataSourceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(dataSource);

            var indexProfileStore = new Mock<ISearchIndexProfileStore>();
            indexProfileStore
                .Setup(store => store.FindByNameAsync(KnowledgeBaseName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(indexProfile);

            var deploymentManager = new Mock<IAIDeploymentManager>();
            deploymentManager
                .Setup(manager => manager.FindByNameAsync(EmbeddingDeploymentName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AIDeployment { ItemId = "deployment-1", Name = EmbeddingDeploymentName });

            var clientFactory = new Mock<IAIClientFactory>();
            clientFactory
                .Setup(factory => factory.CreateEmbeddingGeneratorAsync(It.IsAny<AIDeployment>()))
                .ReturnsAsync(new FixedEmbeddingGenerator());

            var contentManager = new Mock<IDataSourceContentManager>();
            contentManager
                .Setup(manager => manager.SearchAsync(
                    It.IsAny<IIndexProfileInfo>(),
                    It.IsAny<float[]>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IIndexProfileInfo _, float[] _, string _, int _, string providerFilter, CancellationToken _) =>
                {
                    LastFilter = providerFilter;
                    _filters.Add(providerFilter);

                    var results = ResultsForFilter is null ? _results : ResultsForFilter(providerFilter);

                    return Task.FromResult<IEnumerable<DataSourceSearchResult>>(results);
                });

            var textNormalizer = new Mock<IAITextNormalizer>();
            textNormalizer
                .Setup(normalizer => normalizer.NormalizeTitle(It.IsAny<string>()))
                .Returns((string title) => title);

            var filterTranslator = new Mock<IODataFilterTranslator>();
            filterTranslator
                .Setup(translator => translator.Translate(It.IsAny<string>()))
                .Returns((string expression) => "translated:" + expression);

            var services = new ServiceCollection();

            services.AddSingleton(dataSourceStore.Object);
            services.AddSingleton(indexProfileStore.Object);
            services.AddSingleton(deploymentManager.Object);
            services.AddSingleton(clientFactory.Object);
            services.AddSingleton(textNormalizer.Object);
            services.AddKeyedSingleton(ProviderName, contentManager.Object);
            services.AddKeyedSingleton(ProviderName, filterTranslator.Object);

            if (LinkResolver is not null)
            {
                services.AddKeyedSingleton(AIDataSourceSourceTypes.File, LinkResolver);
            }
            services.AddSingleton<IOptionsMonitor<AIDataSourceOptions>>(new TestOptionsMonitor<AIDataSourceOptions>
            {
                CurrentValue = new AIDataSourceOptions(),
            });
            services.AddLogging();

            return services.BuildServiceProvider();
        }
    }

    private sealed class FixedEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions options = null,
            CancellationToken cancellationToken = default)
        {
            var embeddings = values.Select(_ => new Embedding<float>(new[] { 0.25f, 0.5f })).ToList();

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object GetService(Type serviceType, object serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Covers the scope the typed knowledge columns are reserved in: a filter written in the knowledge base's
/// own scope reaches the column, and the same name addressed through the per-row filter bag keeps meaning
/// the caller's own field on every provider.
/// </summary>
public sealed class DataSourceReservedColumnScopeTests
{
    /// <summary>
    /// Verifies that Azure AI Search reads a bag-qualified reserved name as the caller's own field, and an
    /// unqualified one as the column.
    /// </summary>
    [Fact]
    public void AzureTranslate_BagQualifiedReservedName_ReachesTheFilterBag()
    {
        var translator = new AzureAIODataFilterTranslator();

        Assert.Equal("filters/contentType eq 'news'", translator.Translate("filters.contentType eq 'news'"));
        Assert.Equal("contentType eq 'figure'", translator.Translate("contentType eq 'figure'"));
    }

    /// <summary>
    /// Verifies that Elasticsearch reads a bag-qualified reserved name as the caller's own field, and an
    /// unqualified one as the column.
    /// </summary>
    [Fact]
    public void ElasticsearchTranslate_BagQualifiedReservedName_ReachesTheFilterBag()
    {
        var services = new ServiceCollection();
        services.AddCoreElasticsearchServices();

        using var provider = services.BuildServiceProvider();
        var translator = provider.GetRequiredKeyedService<IODataFilterTranslator>(ElasticsearchConstants.ProviderName);

        Assert.Equal("""{"term":{"filters.contentType":"news"}}""", translator.Translate("filters.contentType eq 'news'"));
        Assert.Equal("""{"term":{"contentType":"figure"}}""", translator.Translate("contentType eq 'figure'"));
    }

    /// <summary>
    /// Verifies that PostgreSQL reads a bag-qualified reserved name as the caller's own field, and an
    /// unqualified one as the column.
    /// </summary>
    [Fact]
    public void PostgreSQLTranslate_BagQualifiedReservedName_ReachesTheFilterBag()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddCorePostgreSQLServices();

        using var provider = services.BuildServiceProvider();
        var translator = provider.GetRequiredKeyedService<IODataFilterTranslator>(PostgreSQLConstants.ProviderName);

        Assert.Equal("\"filters\"#>>'{contentType}' = 'news'", translator.Translate("filters.contentType eq 'news'"));
        Assert.Equal("\"contentType\" = 'figure'", translator.Translate("contentType eq 'figure'"));
    }
}
