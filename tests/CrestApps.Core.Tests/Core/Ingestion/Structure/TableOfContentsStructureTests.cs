using CrestApps.Core.Ingestion;
using CrestApps.Core.Tests.Support;
﻿using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Knowledge;
using CrestApps.Core.AI.Ingestion.Knowledge.Structure;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Ingestion.Structure;

/// <summary>
/// Covers splitting a document into the articles it is actually made of. A magazine indexed as one document
/// answers every question with a blend of thirty unrelated subjects; split correctly, each article answers
/// for itself.
/// </summary>
public sealed class TableOfContentsStructureTests
{
    private const double BodySize = 10;
    private const double HeadingSize = 20;
    private const double PageHeight = 800;

    /// <summary>
    /// Verifies that a document with no table of contents is one article, which is exactly what it was before
    /// structure analysis existed.
    /// </summary>
    [Fact]
    public void Analyze_NoTableOfContents_YieldsOneArticle()
    {
        var document = new IngestionDocument("manual.pdf");

        document.Sections.Add(Page(1, Body("Chapter one. The installation procedure begins here.")));
        document.Sections.Add(Page(2, Body("Chapter two. The maintenance procedure begins here.")));

        var structure = Analyze(document);

        var article = Assert.Single(structure.Articles);

        Assert.False(structure.IsInferred);
        Assert.Equal(1, article.PageStart);
        Assert.Equal(2, article.PageEnd);
        Assert.Equal(KnowledgeArticleTypes.Article, article.Type);
    }

    /// <summary>
    /// Verifies that the titles a contents page lists are found on the pages that print them, and that each
    /// article runs until the next one starts.
    /// </summary>
    [Fact]
    public void Analyze_TableOfContents_SplitsAtTheMatchedHeadings()
    {
        var document = BuildMagazine();

        var structure = Analyze(document);

        Assert.True(structure.IsInferred);

        var titled = structure.Articles.Where(article => article.Type == KnowledgeArticleTypes.Article).ToList();

        Assert.Contains(titled, article => article.Title == "The measurement of cold rolled steel");
        Assert.Contains(titled, article => article.Title == "Welding practice in the field");

        var first = titled.Single(article => article.Title == "The measurement of cold rolled steel");
        var second = titled.Single(article => article.Title == "Welding practice in the field");

        Assert.Equal(3, first.PageStart);
        Assert.Equal(4, first.PageEnd);
        Assert.Equal(6, second.PageStart);
    }

    /// <summary>
    /// Verifies that a heading binds to the entry it matches best, not to the first entry it matches well
    /// enough.
    /// </summary>
    /// <remarks>
    /// A contents page grouped by theme lists its titles in its own order, and one that lists a short title
    /// alongside a longer one beginning with the same words gives every heading two entries it could pass
    /// for. Taking the first of them swaps the two articles: each carries the other's title, and every
    /// citation into either names the wrong piece.
    /// </remarks>
    [Fact]
    public void Analyze_EntriesSharingAPrefix_BindEachHeadingToItsOwnEntry()
    {
        var document = new IngestionDocument("journal.pdf");

        // The contents groups the issue by theme rather than by page, so the shorter title is listed first
        // while the longer one is printed first.
        document.Sections.Add(Page(
            1,
            Body("Welding practice  4"),
            Body("Welding practice in the field  2"),
            Body("The corrosion of alloys  6"),
            Body("The fatigue of welded joints  8"),
            Body("Notes from the editor  10")));

        document.Sections.Add(Page(2, Heading("Welding practice in the field"), Body("The longer article opens.")));
        document.Sections.Add(Page(3, Body("The longer article continues.")));
        document.Sections.Add(Page(4, Heading("Welding practice"), Body("The shorter article opens.")));
        document.Sections.Add(Page(5, Body("The shorter article continues.")));

        var structure = Analyze(document);

        var longer = structure.Articles.Single(article => article.Title == "Welding practice in the field");
        var shorter = structure.Articles.Single(article => article.Title == "Welding practice");

        Assert.Equal(2, longer.PageStart);
        Assert.Equal(3, longer.PageEnd);
        Assert.Equal(4, shorter.PageStart);
    }

    /// <summary>
    /// Verifies that the page number printed on a page is captured even when it disagrees with the page's
    /// position in the file, which is the usual case and the reason a citation can name the wrong page.
    /// </summary>
    [Fact]
    public void Analyze_FolioOffsetFromIndex_IsCaptured()
    {
        var document = BuildMagazine();

        var structure = Analyze(document);

        Assert.Equal("14", structure.Folios[3]);
        Assert.Equal("15", structure.Folios[4]);
    }

    /// <summary>
    /// Verifies that a page carrying neither a title nor a running head becomes its own advertisement
    /// article, so its copy never joins the article it interrupted.
    /// </summary>
    [Fact]
    public void Analyze_AdvertPage_BecomesItsOwnExcludedArticle()
    {
        var document = BuildMagazine();

        var structure = Analyze(document);

        var advertisement = Assert.Single(structure.Articles, article => article.Type == KnowledgeArticleTypes.Advertisement);

        Assert.Equal(5, advertisement.PageStart);
        Assert.Equal(5, advertisement.PageEnd);
    }

    /// <summary>
    /// Verifies that an advertisement's objects are stored so the document stays complete, and excluded so
    /// nothing can return them as an answer.
    /// </summary>
    [Fact]
    public void Build_AdvertisementArticle_IsStoredAndExcluded()
    {
        var document = BuildMagazine();
        var structure = Analyze(document);

        var chunks = structure.Articles.ToDictionary(
            article => article.Ordinal,
            article => (IReadOnlyList<string>)["The body of article " + article.Ordinal]);

        var objects = KnowledgeObjectBuilder.Build(
            document,
            new KnowledgeObjectBuildOptions
            {
                FileKey = "0123456789abcdef",
                DataSourceId = "data-source-1",
                Title = "magazine.pdf",
                ContentHash = new string('a', 64),
                Structure = structure,
            },
            chunks);

        var advertisement = structure.Articles.Single(article => article.Type == KnowledgeArticleTypes.Advertisement);
        var article = Assert.Single(objects, entry => entry.CanonicalId == $"article:0123456789abcdef:{advertisement.Ordinal}");

        Assert.Equal(KnowledgeObjectStatus.Excluded, article.Status);
        Assert.All(
            objects.Where(entry => entry.ParentId == article.CanonicalId),
            entry => Assert.Equal(KnowledgeObjectStatus.Excluded, entry.Status));

        // Everything else stays indexable.
        Assert.Contains(objects, entry => entry.ObjectType == KnowledgeObjectTypes.Text && entry.Status == KnowledgeObjectStatus.Ready);
    }

    /// <summary>
    /// Verifies that a document without front matter still produces exactly the objects phase 4 produced, so
    /// nothing that already works changes shape.
    /// </summary>
    [Fact]
    public void Build_NoStructure_MatchesSingleArticleOutput()
    {
        var document = new IngestionDocument("manual.pdf");

        document.Sections.Add(Page(1, Body("The installation procedure begins here.")));

        var options = new KnowledgeObjectBuildOptions
        {
            FileKey = "0123456789abcdef",
            DataSourceId = "data-source-1",
            Title = "manual.pdf",
            ContentHash = new string('a', 64),
        };

        var withoutStructure = KnowledgeObjectBuilder.Build(document, options, ["The installation procedure begins here."]);

        options.Structure = Analyze(document);

        var withStructure = KnowledgeObjectBuilder.Build(
            document,
            options,
            new Dictionary<int, IReadOnlyList<string>>
            {
                [1] = ["The installation procedure begins here."],
            });

        Assert.Equal(
            withoutStructure.Select(entry => entry.CanonicalId),
            withStructure.Select(entry => entry.CanonicalId));
    }

    /// <summary>
    /// Verifies that a document with a table of contents but no running heads at all never has its
    /// continuation pages taken for advertisements. The absence of a label only means something in a document
    /// that prints labels; a report has none, and marking every second page of every chapter as an
    /// advertisement would exclude most of it.
    /// </summary>
    [Fact]
    public void Analyze_NoRunningHeads_KeepsContinuationPagesInTheirArticle()
    {
        var document = new IngestionDocument("report.pdf");

        document.Sections.Add(Page(
            1,
            Body("The measurement of cold rolled steel  3"),
            Body("Welding practice in the field  5"),
            Body("The corrosion of alloys  7"),
            Body("The fatigue of welded joints  9"),
            Body("Notes from the editor  11")));

        document.Sections.Add(Page(2, Heading("The measurement of cold rolled steel"), Body("The first chapter opens.")));
        document.Sections.Add(Page(3, Body("The first chapter continues on a page with no running head.")));
        document.Sections.Add(Page(4, Body("The first chapter continues further.")));
        document.Sections.Add(Page(5, Heading("Welding practice in the field"), Body("The second chapter opens.")));
        document.Sections.Add(Page(6, Body("The second chapter continues.")));

        var structure = Analyze(document);

        Assert.True(structure.IsInferred);
        Assert.DoesNotContain(structure.Articles, article => article.Type == KnowledgeArticleTypes.Advertisement);

        var first = structure.Articles.Single(article => article.Title == "The measurement of cold rolled steel");

        Assert.Equal(2, first.PageStart);
        Assert.Equal(4, first.PageEnd);
    }

    /// <summary>
    /// Verifies that running heads are read in any script, not only in the alphabet of whichever document the
    /// analyzer was first written against.
    /// </summary>
    [Fact]
    public void Analyze_SectionLabelInAnotherScript_IsRecognized()
    {
        var document = new IngestionDocument("journal.pdf");

        document.Sections.Add(Page(
            1,
            Body("The measurement of cold rolled steel  14"),
            Body("Welding practice in the field  18"),
            Body("The corrosion of alloys  22"),
            Body("The fatigue of welded joints  26"),
            Body("Notes from the editor  30")));

        document.Sections.Add(Page(2, Decoration("ΤΕΧΝΟΛΟΓΙΑ", 780), Heading("The measurement of cold rolled steel"), Body("The body of the first article.")));
        document.Sections.Add(Page(3, Decoration("ΤΕΧΝΟΛΟΓΙΑ", 780), Body("More of the first article.")));

        var structure = Analyze(document);

        Assert.Contains(structure.Articles, article => article.SectionLabel == "ΤΕΧΝΟΛΟΓΙΑ");
    }

    /// <summary>
    /// Verifies that a document which labels only a few of its pages is not read as mostly advertisements.
    /// </summary>
    /// <remarks>
    /// The absence of a running head is evidence only where running heads are the norm. A book that prints
    /// one on its chapter openers and nowhere else says nothing by omitting it, and an advertisement article
    /// is excluded from the index — so getting this wrong does not return a worse answer, it silently drops
    /// most of the book out of the knowledge base.
    /// </remarks>
    [Fact]
    public void Analyze_RunningHeadsOnlyOnChapterOpeners_YieldsNoAdvertisements()
    {
        var document = new IngestionDocument("book.pdf");

        document.Sections.Add(Page(
            1,
            Body("The measurement of cold rolled steel  3"),
            Body("Welding practice in the field  8"),
            Body("The corrosion of alloys  12"),
            Body("The fatigue of welded joints  16"),
            Body("Notes from the editor  20")));

        // Only the two chapter openers carry a running head; every continuation page carries none.
        document.Sections.Add(Page(
            2,
            Decoration("CHAPTER ONE", 780),
            Heading("The measurement of cold rolled steel"),
            Body("The first chapter opens with the method used to measure the sheet.")));

        for (var pageNumber = 3; pageNumber <= 7; pageNumber++)
        {
            document.Sections.Add(Page(pageNumber, Body($"The first chapter continues on page {pageNumber}.")));
        }

        document.Sections.Add(Page(
            8,
            Decoration("CHAPTER TWO", 780),
            Heading("Welding practice in the field"),
            Body("The second chapter opens with the equipment a field welder carries.")));

        for (var pageNumber = 9; pageNumber <= 13; pageNumber++)
        {
            document.Sections.Add(Page(pageNumber, Body($"The second chapter continues on page {pageNumber}.")));
        }

        var structure = Analyze(document);

        Assert.DoesNotContain(structure.Articles, article => article.Type == KnowledgeArticleTypes.Advertisement);

        var first = structure.Articles.Single(article => article.Title == "The measurement of cold rolled steel");

        Assert.Equal(2, first.PageStart);
        Assert.Equal(7, first.PageEnd);
    }

    /// <summary>
    /// Verifies that structure is keyed by the page a section was read from, not by its position.
    /// </summary>
    /// <remarks>
    /// A reader drops a page that produced nothing, so the two stop agreeing the moment a document contains
    /// a blank leaf. Everything downstream — a figure above all — carries the real page number, so keying by
    /// position shifts every article boundary and every printed page number after the gap, and a citation
    /// then names a page the reader never saw.
    /// </remarks>
    [Fact]
    public void Analyze_DocumentWithAMissingPage_KeepsFoliosOnTheirRealPages()
    {
        var document = new IngestionDocument("magazine.pdf");

        document.Sections.Add(Page(1, Body("The cover of the issue.")));

        document.Sections.Add(Page(
            2,
            Body("The measurement of cold rolled steel  14"),
            Body("Welding practice in the field  18"),
            Body("The corrosion of alloys  22"),
            Body("The fatigue of welded joints  26"),
            Body("Notes from the editor  30")));

        // PDF page 3 produced nothing, so no section carries it. Section index 2 is PDF page 4.
        document.Sections.Add(Page(
            4,
            Decoration("TECHNOLOGY", 780),
            Decoration("14", 20),
            Heading("The measurement of cold rolled steel"),
            Body("The first article opens with the method used to measure the sheet.")));

        document.Sections.Add(Page(
            5,
            Decoration("TECHNOLOGY", 780),
            Decoration("15", 20),
            Body("The first article continues with the results of the measurement.")));

        var structure = Analyze(document);

        Assert.Equal("14", structure.Folios[4]);
        Assert.Equal("15", structure.Folios[5]);
        Assert.False(structure.Folios.ContainsKey(3));

        var article = structure.Articles.Single(item => item.Title == "The measurement of cold rolled steel");

        Assert.Equal(4, article.PageStart);
    }

    /// <summary>
    /// Verifies that a document carrying no type sizes still splits into its articles.
    /// </summary>
    /// <remarks>
    /// A provider-backed reader supplies structure without font metrics, so the test that says "this line is
    /// big enough to be a heading" cannot run. With it skipped, every line on the contents page matched its
    /// own entry and every boundary landed on that one page, which collapsed the whole document into a
    /// single article — the opposite of what a table of contents was read for.
    /// </remarks>
    [Fact]
    public void Analyze_NoPointSizes_StillSplitsOnTheRealPages()
    {
        var document = new IngestionDocument("magazine.pdf");

        document.Sections.Add(Sized(1, Plain("The cover of the issue.")));

        document.Sections.Add(Sized(
            2,
            Plain("The measurement of cold rolled steel  3"),
            Plain("Welding practice in the field  5"),
            Plain("The corrosion of alloys  7"),
            Plain("The fatigue of welded joints  9"),
            Plain("Notes from the editor  11")));

        document.Sections.Add(Sized(3, Plain("The measurement of cold rolled steel"), Plain("The first article opens.")));
        document.Sections.Add(Sized(4, Plain("The first article continues.")));
        document.Sections.Add(Sized(5, Plain("Welding practice in the field"), Plain("The second article opens.")));
        document.Sections.Add(Sized(6, Plain("The second article continues.")));

        var structure = Analyze(document);

        var first = structure.Articles.Single(article => article.Title == "The measurement of cold rolled steel");

        Assert.Equal(3, first.PageStart);
        Assert.Equal(4, first.PageEnd);
        Assert.Contains(structure.Articles, article => article.Title == "Welding practice in the field");
    }

    /// <summary>
    /// Verifies that a publication with no readable contents page still splits on its own headings.
    /// </summary>
    /// <remarks>
    /// A contents page is the answer key, not a precondition. Plenty of publications have none that can be
    /// read — no title and page number share a line, or what looks like a contents page is a parts list —
    /// and answering "one article" for a whole magazine makes every question about one article retrieve a
    /// blend of all of them.
    /// </remarks>
    [Fact]
    public void Analyze_NoReadableContentsPage_SplitsOnHeadings()
    {
        var document = new IngestionDocument("magazine.pdf");

        document.Sections.Add(Page(1, Body("The cover of the issue.")));

        // Four articles, each opening with a heading at the top of its page.
        document.Sections.Add(Page(2, TopHeading("The measurement of cold rolled steel"), Body("The first article opens.")));
        document.Sections.Add(Page(3, Body("The first article continues.")));
        document.Sections.Add(Page(4, TopHeading("Welding practice in the field"), Body("The second article opens.")));
        document.Sections.Add(Page(5, TopHeading("The corrosion of alloys"), Body("The third article opens.")));
        document.Sections.Add(Page(6, Body("The third article continues.")));
        document.Sections.Add(Page(7, TopHeading("The fatigue of welded joints"), Body("The fourth article opens.")));

        var structure = Analyze(document);

        Assert.True(structure.IsInferred);

        var titled = structure.Articles
            .Where(article => article.Type == KnowledgeArticleTypes.Article && article.Title != "magazine.pdf")
            .ToList();

        Assert.Equal(4, titled.Count);

        var first = titled.Single(article => article.Title == "The measurement of cold rolled steel");

        Assert.Equal(2, first.PageStart);
        Assert.Equal(3, first.PageEnd);

        var third = titled.Single(article => article.Title == "The corrosion of alloys");

        Assert.Equal(5, third.PageStart);
        Assert.Equal(6, third.PageEnd);
    }

    /// <summary>
    /// Verifies that a heading part way down a page does not split the article it sits inside.
    /// </summary>
    /// <remarks>
    /// A title opens a page. Treating every large line as a boundary would cut an article in two at each of
    /// its own subheadings and at every pull quote.
    /// </remarks>
    [Fact]
    public void Analyze_HeadingsBelowTheTitleBand_DoNotSplitTheDocument()
    {
        var document = new IngestionDocument("report.pdf");

        document.Sections.Add(Page(1, Body("The cover.")));

        for (var pageNumber = 2; pageNumber <= 7; pageNumber++)
        {
            document.Sections.Add(Page(
                pageNumber,
                Body($"Body text on page {pageNumber}."),
                MidPageHeading($"A subheading on page {pageNumber}")));
        }

        var structure = Analyze(document);

        Assert.Single(structure.Articles);
    }

    private static IngestionDocumentParagraph TopHeading(string text)
    {
        var paragraph = Heading(text);
        paragraph.Metadata[ElementMetadataKeys.BoundingBox] = new[] { 50d, PageHeight - 40, 500d, PageHeight - 10 };

        return paragraph;
    }

    private static IngestionDocumentParagraph MidPageHeading(string text)
    {
        var paragraph = Heading(text);
        paragraph.Metadata[ElementMetadataKeys.BoundingBox] = new[] { 50d, PageHeight * 0.4, 500d, PageHeight * 0.45 };

        return paragraph;
    }

    private static IngestionDocumentSection Sized(int pageNumber, params IngestionDocumentElement[] elements)
    {
        var section = new IngestionDocumentSection
        {
            PageNumber = pageNumber,
        };

        section.Metadata[ElementMetadataKeys.PageHeight] = PageHeight;
        section.Metadata[ElementMetadataKeys.PageWidth] = 600d;

        foreach (var element in elements)
        {
            element.PageNumber = pageNumber;
            section.Elements.Add(element);
        }

        return section;
    }

    private static IngestionDocumentParagraph Plain(string text)
    {
        return new IngestionDocumentParagraph(text)
        {
            Text = text,
        };
    }

    private static DocumentStructure Analyze(IngestionDocument document)
    {
        return StructureAnalyzers.Default().Analyze(document);
    }

    /// <summary>
    /// Builds a synthetic magazine: a contents page, two titled articles with running heads and printed page
    /// numbers, and one page carrying neither.
    /// </summary>
    private static IngestionDocument BuildMagazine()
    {
        var document = new IngestionDocument("magazine.pdf");

        document.Sections.Add(Page(1, Body("The cover of the issue.")));

        document.Sections.Add(Page(
            2,
            Body("The measurement of cold rolled steel  14"),
            Body("Welding practice in the field  18"),
            Body("The corrosion of alloys  22"),
            Body("The fatigue of welded joints  26"),
            Body("Notes from the editor  30")));

        document.Sections.Add(Page(
            3,
            Decoration("TECHNOLOGY", 780),
            Decoration("14", 20),
            Heading("The measurement of cold rolled steel"),
            Body("The first article opens with the method used to measure the sheet.")));

        document.Sections.Add(Page(
            4,
            Decoration("TECHNOLOGY", 780),
            Decoration("15", 20),
            Body("The first article continues with the results of the measurement.")));

        document.Sections.Add(Page(
            5,
            Body("Buy the finest rolling equipment available anywhere today.")));

        document.Sections.Add(Page(
            6,
            Decoration("TECHNOLOGY", 780),
            Decoration("18", 20),
            Heading("Welding practice in the field"),
            Body("The second article opens with the equipment a field welder carries.")));

        return document;
    }

    private static IngestionDocumentSection Page(int pageNumber, params IngestionDocumentElement[] elements)
    {
        var section = new IngestionDocumentSection
        {
            PageNumber = pageNumber,
        };

        section.Metadata[ElementMetadataKeys.PageHeight] = PageHeight;
        section.Metadata[ElementMetadataKeys.PageWidth] = 600d;

        foreach (var element in elements)
        {
            element.PageNumber = pageNumber;
            section.Elements.Add(element);
        }

        return section;
    }

    private static IngestionDocumentParagraph Body(string text)
    {
        var paragraph = new IngestionDocumentParagraph(text)
        {
            Text = text,
        };

        paragraph.Metadata[ElementMetadataKeys.ModalPointSize] = BodySize;

        return paragraph;
    }

    private static IngestionDocumentParagraph Heading(string text)
    {
        var paragraph = new IngestionDocumentParagraph(text)
        {
            Text = text,
        };

        paragraph.Metadata[ElementMetadataKeys.ModalPointSize] = HeadingSize;

        return paragraph;
    }

    private static IngestionDocumentParagraph Decoration(string text, double bottom)
    {
        var paragraph = new IngestionDocumentParagraph(text)
        {
            Text = text,
        };

        paragraph.Metadata[ElementMetadataKeys.ModalPointSize] = BodySize;
        paragraph.Metadata[ElementMetadataKeys.IsDecoration] = true;
        paragraph.Metadata[ElementMetadataKeys.BoundingBox] = new[] { 50d, bottom, 200d, bottom + 12 };

        return paragraph;
    }
}
