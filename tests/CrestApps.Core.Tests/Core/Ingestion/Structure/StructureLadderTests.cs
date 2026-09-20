using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Knowledge.Structure;
using CrestApps.Core.DataIngestion;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Ingestion.Structure;

/// <summary>
/// Covers which rung of the ladder answers, and that a stated answer is never passed over for an inferred
/// one.
/// </summary>
/// <remarks>
/// A split that comes out wrong is far easier to argue with when the answer says what it was based on. These
/// tests hold that report honest, and hold the order of authority in place.
/// </remarks>
public sealed class StructureLadderTests
{
    [Fact]
    public void Analyze_ReportsStatedHeadingsWhenTheFormatNamesThem()
    {
        var structure = Analyze(HtmlIngestionDocumentReader.Read(
            "<html><body><h1>Guide</h1><p>Intro.</p><h2>Install</h2><p>Steps.</p></body></html>",
            "https://example.test/guide"));

        Assert.Equal(DocumentStructureSources.StatedHeadings, structure.Source);
        Assert.True(structure.IsInferred);
    }

    [Fact]
    public void Analyze_ReportsWholeWhenNothingCanBeWorkedOut()
    {
        var document = new IngestionDocument("notes.txt");
        var section = new IngestionDocumentSection();

        section.Elements.Add(new IngestionDocumentParagraph("Just a paragraph.")
        {
            Text = "Just a paragraph.",
        });

        document.Sections.Add(section);

        var structure = Analyze(document);

        Assert.Equal(DocumentStructureSources.Whole, structure.Source);
        Assert.False(structure.IsInferred);
        Assert.Single(structure.Articles);
    }

    [Fact]
    public void Analyze_PrefersAStatedHeadingOverAnInferredOne()
    {
        // The element that states a level is set in body-sized type, and the one that states nothing is set
        // large. Reading type size would divide at the wrong one.
        var document = new IngestionDocument("report.pdf");

        document.Sections.Add(BuildSection(1, ("Chapter One", 10d, 1), ("Body text here.", 10d, 0)));
        document.Sections.Add(BuildSection(2, ("A LARGE PULL QUOTE", 30d, 0), ("More body text.", 10d, 0)));
        document.Sections.Add(BuildSection(3, ("Chapter Two", 10d, 1), ("Closing text.", 10d, 0)));

        var structure = Analyze(document);

        Assert.Equal(DocumentStructureSources.StatedHeadings, structure.Source);
        Assert.Equal(["Chapter One", "Chapter Two"], structure.Articles.Select(article => article.Title));
    }

    [Fact]
    public void Analyze_EmptyDocumentIsWhole()
    {
        var structure = Analyze(new IngestionDocument("empty.pdf"));

        Assert.Equal(DocumentStructureSources.Whole, structure.Source);
    }

    private static IngestionDocumentSection BuildSection(int page, params (string Text, double Size, int HeadingLevel)[] elements)
    {
        var section = new IngestionDocumentSection
        {
            PageNumber = page,
        };

        section.Metadata[ElementMetadataKeys.PageHeight] = 842d;
        section.Metadata[ElementMetadataKeys.PageWidth] = 595d;

        foreach (var (text, size, headingLevel) in elements)
        {
            var element = new IngestionDocumentParagraph(text)
            {
                Text = text,
                PageNumber = page,
            };

            element.Metadata[ElementMetadataKeys.ModalPointSize] = size;
            element.Metadata[ElementMetadataKeys.BoundingBox] = new[] { 50d, 780d, 545d, 800d };

            if (headingLevel > 0)
            {
                element.Metadata[ElementMetadataKeys.HeadingLevel] = headingLevel;
            }

            section.Elements.Add(element);
        }

        return section;
    }

    private static DocumentStructure Analyze(IngestionDocument document)
    {
        return new TocSeededStructureAnalyzer(NullLogger<TocSeededStructureAnalyzer>.Instance).Analyze(document);
    }
}
