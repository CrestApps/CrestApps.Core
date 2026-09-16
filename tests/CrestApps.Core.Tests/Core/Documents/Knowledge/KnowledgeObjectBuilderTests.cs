using CrestApps.Core.AI.Documents.Ingestion;
using CrestApps.Core.AI.Documents.Ingestion.Processors;
using CrestApps.Core.AI.Documents.Knowledge;
using CrestApps.Core.AI.Documents.Knowledge.Structure;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.Tests.Core.Documents.Knowledge;

public sealed class KnowledgeObjectBuilderTests
{
    /// <summary>
    /// Verifies that identical bytes produce identical identifiers, which is what makes re-ingesting a file
    /// replace what it produced last time instead of duplicating it.
    /// </summary>
    [Fact]
    public void Build_SameFileKey_ProducesStableIdentifiers()
    {
        var first = Build(CreateDocument(), ["Chunk one."]);
        var second = Build(CreateDocument(), ["Chunk one."]);

        Assert.Equal(
            first.Select(entry => entry.CanonicalId),
            second.Select(entry => entry.CanonicalId));
    }

    /// <summary>
    /// Verifies that every object knows the document it belongs to and what it hangs off, so a hit on a chunk
    /// of text can be widened to its article and cited against its document.
    /// </summary>
    [Fact]
    public void Build_EveryObject_CarriesRootAndParent()
    {
        var objects = Build(CreateDocument(), ["Chunk one."]);

        var document = Assert.Single(objects, entry => entry.ObjectType == KnowledgeContentTypes.Document);
        var article = Assert.Single(objects, entry => entry.ObjectType == KnowledgeContentTypes.Article);

        Assert.Equal(document.CanonicalId, document.RootId);
        Assert.Null(document.ParentId);
        Assert.Equal(document.CanonicalId, article.ParentId);

        Assert.All(objects, entry => Assert.Equal(document.CanonicalId, entry.RootId));
        Assert.All(
            objects.Where(entry => entry.ObjectType is KnowledgeContentTypes.Text or KnowledgeContentTypes.Figure or KnowledgeContentTypes.Chart or KnowledgeContentTypes.Table),
            entry => Assert.Equal(article.CanonicalId, entry.ParentId));
    }

    /// <summary>
    /// Verifies that a chunk reports the pages it actually spans, so a citation names the right page.
    /// </summary>
    [Fact]
    public void Build_TextChunk_CarriesThePagesItSpans()
    {
        var document = new IngestionDocument("report.pdf");

        document.Sections.Add(Page(1, Paragraph("Page one body.", 1)));
        document.Sections.Add(Page(2, Paragraph("Page two body.", 2)));

        var objects = Build(document, ["Page one body.", "Page two body."]);
        var chunks = objects.Where(entry => entry.ObjectType == KnowledgeContentTypes.Text).ToList();

        Assert.Equal(2, chunks.Count);
        Assert.Equal(1, chunks[0].PageStart);
        Assert.Equal(2, chunks[1].PageStart);
    }

    /// <summary>
    /// Verifies that a figure kept only for its caption is finished, and one still owed a transcription says
    /// so, because that is what puts it on the backfill queue.
    /// </summary>
    [Fact]
    public void Build_FigureStatus_ReflectsWhetherATranscriptionIsStillOwed()
    {
        var document = new IngestionDocument("report.pdf");

        document.Sections.Add(Page(
            1,
            Paragraph("Body.", 1),
            Figure("report.pdf-p1-1", FigureTiers.CaptionOnly, "A view of the rig.", description: null),
            Figure("report.pdf-p1-2", FigureTiers.Describe, "Figure 2. The measurements.", description: null),
            Figure("report.pdf-p1-3", FigureTiers.Describe, "Figure 3. The other one.", description: "Already transcribed.")));

        var objects = Build(document, ["Body."]);
        var figures = objects.Where(entry => entry.ObjectType is KnowledgeContentTypes.Figure or KnowledgeContentTypes.Chart).ToList();

        Assert.Equal(3, figures.Count);
        Assert.Equal(KnowledgeObjectStatus.Ready, figures[0].Status);
        Assert.Equal(KnowledgeObjectStatus.PendingDescription, figures[1].Status);
        Assert.Equal(KnowledgeObjectStatus.Ready, figures[2].Status);
    }

    /// <summary>
    /// Verifies that a figure salience dropped is never stored. Its bytes were not kept either.
    /// </summary>
    [Fact]
    public void Build_SkippedFigure_IsNotStored()
    {
        var document = new IngestionDocument("report.pdf");

        document.Sections.Add(Page(
            1,
            Paragraph("Body.", 1),
            Figure("report.pdf-p1-1", FigureTiers.Skip, caption: null, description: null)));

        var objects = Build(document, ["Body."]);

        Assert.DoesNotContain(objects, entry => entry.ObjectType is KnowledgeContentTypes.Figure or KnowledgeContentTypes.Chart);
    }

    /// <summary>
    /// Verifies that a figure whose caption says it is a chart is stored as one, and is explicit that its
    /// values are not machine-readable rather than implying it has numbers.
    /// </summary>
    [Fact]
    public void Build_ChartFigure_IsTypedAsChartAndDescriptive()
    {
        var document = new IngestionDocument("report.pdf");

        document.Sections.Add(Page(
            1,
            Paragraph("Body.", 1),
            Figure("report.pdf-p1-1", FigureTiers.Describe, "Figure 1. A bar chart of the values.", "A bar chart.")));

        var objects = Build(document, ["Body."]);
        var chart = Assert.Single(objects, entry => entry.ObjectType == KnowledgeContentTypes.Chart);

        Assert.True(chart.TryGet<ChartDetails>(out var details));
        Assert.Equal(ChartValueConfidence.Descriptive, details.ValueConfidence);
        Assert.Empty(details.Series);
    }

    /// <summary>
    /// Verifies that a table's text carries every cell, each labelled by its column, so a row retrieved on
    /// its own still says what its values mean.
    /// </summary>
    [Fact]
    public void Build_Table_ContentIncludesEveryCellWithItsColumn()
    {
        var cells = new IngestionDocumentElement[2, 2];
        cells[0, 0] = new IngestionDocumentParagraph("a") { Text = "Material" };
        cells[0, 1] = new IngestionDocumentParagraph("b") { Text = "Strength" };
        cells[1, 0] = new IngestionDocumentParagraph("c") { Text = "Steel" };
        cells[1, 1] = new IngestionDocumentParagraph("d") { Text = "400" };

        var table = new IngestionDocumentTable("markdown", cells)
        {
            Text = "markdown",
            PageNumber = 1,
        };

        var document = new IngestionDocument("report.pdf");
        document.Sections.Add(Page(1, Paragraph("Body.", 1), table));

        var objects = Build(document, ["Body."]);
        var entry = Assert.Single(objects, item => item.ObjectType == KnowledgeContentTypes.Table);

        Assert.Contains("Material | Strength", entry.Content, StringComparison.Ordinal);
        Assert.Contains("Material=Steel; Strength=400", entry.Content, StringComparison.Ordinal);
        Assert.True(entry.TryGet<TableDetails>(out var details));
        Assert.Equal(["Material", "Strength"], details.Columns);
    }

    /// <summary>
    /// Verifies that artwork placed twice becomes one object. The repeat carries the same bytes under the same
    /// hash, and storing it again would answer a question with the same figure twice.
    /// </summary>
    [Fact]
    public void Build_DuplicateFigure_IsNotStoredTwice()
    {
        var document = new IngestionDocument("report.pdf");
        var repeat = Figure("report.pdf-p2-1", FigureTiers.CaptionOnly, "Figure 1. The rig.", description: null);

        repeat.Metadata[FigureMetadataKeys.DuplicateOf] = "report.pdf-p1-1";

        document.Sections.Add(Page(
            1,
            Paragraph("Body.", 1),
            Figure("report.pdf-p1-1", FigureTiers.CaptionOnly, "Figure 1. The rig.", description: null)));
        document.Sections.Add(Page(2, repeat));

        var objects = Build(document, ["Body."]);

        Assert.Single(objects, entry => entry.ObjectType == KnowledgeContentTypes.Figure);
    }

    /// <summary>
    /// Verifies that a chart is recognized in the language its caption is written in, not only in English.
    /// </summary>
    [Fact]
    public void Build_ChartCaptionInAnotherLanguage_IsTypedAsChart()
    {
        var document = new IngestionDocument("report.pdf");

        document.Sections.Add(Page(
            1,
            Paragraph("Body.", 1),
            Figure("report.pdf-p1-1", FigureTiers.Describe, "3. ábra. Fajlagos fűtési energia grafikon.", description: null)));

        var objects = Build(document, ["Body."]);

        Assert.Single(objects, entry => entry.ObjectType == KnowledgeContentTypes.Chart);
    }

    /// <summary>
    /// Verifies that a caption is not also emitted as body text, and that page furniture never reaches the
    /// article at all.
    /// </summary>
    [Fact]
    public void Build_CaptionsAndDecoration_AreNotArticleText()
    {
        var caption = Paragraph("Figure 1. The caption.", 1);
        caption.Metadata[ElementMetadataKeys.IsCaptionFor] = "report.pdf-p1-1";

        var runningHead = Paragraph("QUARTERLY REVIEW", 1);
        runningHead.Metadata[ElementMetadataKeys.IsDecoration] = true;

        var document = new IngestionDocument("report.pdf");
        document.Sections.Add(Page(1, runningHead, Paragraph("Real body text.", 1), caption));

        var objects = Build(document, ["Real body text."]);
        var article = Assert.Single(objects, entry => entry.ObjectType == KnowledgeContentTypes.Article);

        Assert.DoesNotContain("QUARTERLY REVIEW", article.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Figure 1. The caption.", article.Content, StringComparison.Ordinal);
        Assert.Contains("Real body text.", article.Content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a document whose front matter named its publication is cited by the issue rather than by
    /// the file it arrived in. Two issues of one publication are two files, and only the masthead says which
    /// issue this is.
    /// </summary>
    [Fact]
    public void Build_PublicationMetadata_NamesTheIssueInsteadOfTheFile()
    {
        var objects = Build(CreateDocument(), ["Chunk one."], CreatePublication());
        var document = Assert.Single(objects, entry => entry.ObjectType == KnowledgeContentTypes.Document);

        Assert.Equal("The Sample Review, Volume 4, Number 2, Spring 1999", document.Title);
        Assert.DoesNotContain("report.pdf", document.Title, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a document that named no publication is still cited by its file name, with nothing
    /// invented in place of a masthead it does not have.
    /// </summary>
    [Fact]
    public void Build_NoPublicationMetadata_KeepsTheFileNameAndCarriesNoDetail()
    {
        var objects = Build(CreateDocument(), ["Chunk one."]);
        var document = Assert.Single(objects, entry => entry.ObjectType == KnowledgeContentTypes.Document);

        Assert.Equal("report.pdf", document.Title);
        Assert.All(objects, entry => Assert.False(entry.TryGet<PublicationDetails>(out _)));
    }

    /// <summary>
    /// Verifies that a volume printed without a publication title names nothing on its own, so the file name
    /// is left to do the naming rather than a citation reading "Volume 4".
    /// </summary>
    [Fact]
    public void Build_PublicationMetadataWithoutATitle_KeepsTheFileName()
    {
        var objects = Build(CreateDocument(), ["Chunk one."], new PublicationDetails { Volume = "Volume 4" });
        var document = Assert.Single(objects, entry => entry.ObjectType == KnowledgeContentTypes.Document);

        Assert.Equal("report.pdf", document.Title);
    }

    /// <summary>
    /// Verifies that every object carries the issue it was printed in, not just the document object. An
    /// object is indexed on its own, and an incremental sync of one figure never reads its document.
    /// </summary>
    [Fact]
    public void Build_PublicationMetadata_IsCarriedByEveryObject()
    {
        var document = new IngestionDocument("report.pdf");

        document.Sections.Add(Page(
            1,
            Paragraph("Chunk one.", 1),
            Figure("report.pdf-p1-1", FigureTiers.CaptionOnly, "A view of the rig.", description: null)));

        var objects = Build(document, ["Chunk one."], CreatePublication());

        Assert.Contains(objects, entry => entry.ObjectType == KnowledgeContentTypes.Text);
        Assert.Contains(objects, entry => entry.ObjectType == KnowledgeContentTypes.Figure);
        Assert.All(objects, entry =>
        {
            Assert.True(entry.TryGet<PublicationDetails>(out var publication));
            Assert.Equal("The Sample Review", publication.PublicationTitle);
            Assert.Equal("Number 2", publication.Issue);
            Assert.Equal("Spring 1999", publication.Date);
        });
    }

    /// <summary>
    /// Verifies that an article the analysis could not name is cited by the issue rather than by the file
    /// name it carries as a placeholder. The chunks of text hanging off it are most of a document's rows, so
    /// a citation that names the file there names it nearly everywhere.
    /// </summary>
    [Fact]
    public void Build_UnnamedArticle_IsTitledByTheIssueRatherThanTheFile()
    {
        var document = CreateDocument();

        var objects = KnowledgeObjectBuilder.Build(
            document,
            new KnowledgeObjectBuildOptions
            {
                FileKey = "0123456789abcdef",
                DataSourceId = "data-source-1",
                Title = "report.pdf",
                ContentHash = new string('a', 64),
                Publication = CreatePublication(),

                // What the analysis hands back for a document it could not split: one article carrying the
                // document's own identifier in place of a title.
                Structure = new DocumentStructure
                {
                    Articles =
                    [
                        new DocumentArticle
                        {
                            Ordinal = 1,
                            Title = document.Identifier,
                            PageStart = 1,
                            PageEnd = 1,
                            Type = KnowledgeArticleTypes.Article,
                        },
                    ],
                },
            },
            ["Chunk one."]);

        var article = Assert.Single(objects, entry => entry.ObjectType == KnowledgeContentTypes.Article);
        var text = Assert.Single(objects, entry => entry.ObjectType == KnowledgeContentTypes.Text);

        Assert.Equal("The Sample Review, Volume 4, Number 2, Spring 1999", article.Title);
        Assert.Equal("The Sample Review, Volume 4, Number 2, Spring 1999", text.Title);
    }

    /// <summary>
    /// Verifies that a figure nobody captioned is cited by the issue as well, rather than by the file name it
    /// used to fall back to.
    /// </summary>
    [Fact]
    public void Build_UncaptionedFigure_IsTitledByTheIssue()
    {
        var document = new IngestionDocument("report.pdf");

        document.Sections.Add(Page(
            1,
            Paragraph("Chunk one.", 1),
            Figure("report.pdf-p1-1", FigureTiers.CaptionOnly, caption: null, description: null)));

        var objects = Build(document, ["Chunk one."], CreatePublication());
        var figure = Assert.Single(objects, entry => entry.ObjectType == KnowledgeContentTypes.Figure);

        Assert.Equal("The Sample Review, Volume 4, Number 2, Spring 1999", figure.Title);
    }

    private static IReadOnlyList<KnowledgeObject> Build(
        IngestionDocument document,
        IReadOnlyList<string> chunks,
        PublicationDetails publication = null)
    {
        return KnowledgeObjectBuilder.Build(
            document,
            new KnowledgeObjectBuildOptions
            {
                FileKey = "0123456789abcdef",
                DataSourceId = "data-source-1",
                Title = "report.pdf",
                ContentHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                Publication = publication,
            },
            chunks);
    }

    private static PublicationDetails CreatePublication()
    {
        return new PublicationDetails
        {
            PublicationTitle = "The Sample Review",
            Publisher = "Sample Press",
            Volume = "Volume 4",
            Issue = "Number 2",
            Date = "Spring 1999",
        };
    }

    private static IngestionDocument CreateDocument()
    {
        var document = new IngestionDocument("report.pdf");

        document.Sections.Add(Page(1, Paragraph("Chunk one.", 1)));

        return document;
    }

    private static IngestionDocumentSection Page(int pageNumber, params IngestionDocumentElement[] elements)
    {
        var section = new IngestionDocumentSection
        {
            PageNumber = pageNumber,
        };

        foreach (var element in elements)
        {
            section.Elements.Add(element);
        }

        return section;
    }

    private static IngestionDocumentParagraph Paragraph(string text, int pageNumber)
    {
        return new IngestionDocumentParagraph(text)
        {
            Text = text,
            PageNumber = pageNumber,
        };
    }

    private static IngestionDocumentImage Figure(string figureId, string tier, string caption, string description)
    {
        var image = new IngestionDocumentImage($"![]({figureId})")
        {
            Content = new byte[] { 1, 2, 3, 4 },
            MediaType = "image/png",
            PageNumber = 1,
            AlternativeText = description,
        };

        image.Metadata[FigureMetadataKeys.Id] = figureId;
        image.Metadata[FigureMetadataKeys.Tier] = tier;
        image.Metadata[FigureMetadataKeys.ContentHash] = "hash-" + figureId;

        if (caption != null)
        {
            image.Metadata[FigureMetadataKeys.Caption] = caption;
        }

        return image;
    }
}
