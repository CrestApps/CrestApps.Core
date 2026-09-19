using Azure.AI.DocumentIntelligence;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Processors;
using CrestApps.Core.Azure.DocumentIntelligence.Services;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.Tests.Core.Azure.DocumentIntelligence;

/// <summary>
/// Covers the mapping from a Document Intelligence analysis onto the element model. Every fixture is written
/// by hand; no analysed document is ever captured into the repository.
/// </summary>
public sealed class DocumentIntelligenceDocumentMapperTests
{
    /// <summary>
    /// Verifies that a page header and a page footer arrive as decoration rather than as body text, which is
    /// the judgement the local reader has to make with a heuristic.
    /// </summary>
    [Fact]
    public void Map_ParagraphRoles_BecomeHeadersFootersAndBody()
    {
        var result = CreateResult(paragraphs:
        [
            Paragraph(ParagraphRole.PageHeader, "QUARTERLY REVIEW", offset: 0),
            Paragraph(null, "Body paragraph one.", offset: 20),
            Paragraph(ParagraphRole.PageFooter, "Page 4 of 23", offset: 60),
        ]);

        var document = DocumentIntelligenceDocumentMapper.Map(result, "doc.pdf");
        var elements = document.EnumerateContent().ToList();

        Assert.Equal(3, elements.Count);

        var header = Assert.IsType<IngestionDocumentHeader>(elements[0]);
        Assert.Equal("QUARTERLY REVIEW", header.Text);
        Assert.True((bool)header.Metadata[ElementMetadataKeys.IsDecoration]);

        Assert.IsType<IngestionDocumentParagraph>(elements[1]);
        Assert.IsType<IngestionDocumentFooter>(elements[2]);
    }

    /// <summary>
    /// Verifies that a heading the service identified is recorded as such, so a later structure pass does not
    /// have to guess from font sizes.
    /// </summary>
    [Fact]
    public void Map_SectionHeading_CarriesSectionLabel()
    {
        var result = CreateResult(paragraphs:
        [
            Paragraph(ParagraphRole.SectionHeading, "Measurement method", offset: 0),
        ]);

        var element = Assert.Single(DocumentIntelligenceDocumentMapper.Map(result, "doc.pdf").EnumerateContent());

        Assert.Equal("Measurement method", element.GetMetadataString(ElementMetadataKeys.SectionLabel));
    }

    /// <summary>
    /// Verifies that a figure arrives with the caption the service matched to it, marked as coming from a
    /// provider so nothing downstream tries to improve on it.
    /// </summary>
    [Fact]
    public void Map_Figure_CarriesProviderCaptionAndOrdinal()
    {
        var result = CreateResult(figures:
        [
            DocumentIntelligenceModelFactory.DocumentFigure(
                boundingRegions: [Region(1, 1, 2, 3, 4)],
                spans: [DocumentIntelligenceModelFactory.DocumentSpan(10, 5)],
                elements: null,
                caption: DocumentIntelligenceModelFactory.DocumentCaption(
                    "Figure 3. Measured values against the model.",
                    boundingRegions: [Region(1, 1, 4, 3, 4.2f)],
                    spans: [DocumentIntelligenceModelFactory.DocumentSpan(40, 44)],
                    elements: null),
                footnotes: null,
                id: "1.1"),
        ]);

        var image = Assert.IsType<IngestionDocumentImage>(
            Assert.Single(DocumentIntelligenceDocumentMapper.Map(result, "doc.pdf").EnumerateContent()));

        Assert.Equal("doc.pdf-p1-1", image.GetFigureId());
        Assert.Equal("Figure 3. Measured values against the model.", image.GetMetadataString(FigureMetadataKeys.Caption));
        Assert.Equal(CaptionSources.Provider, image.GetMetadataString(FigureMetadataKeys.CaptionSource));
        Assert.Equal(3, image.Metadata[FigureMetadataKeys.Ordinal]);
        Assert.Equal("1.1", image.GetMetadataString(DocumentIntelligenceDocumentMapper.ProviderFigureIdKey));
    }

    /// <summary>
    /// Verifies that a table arrives with its real cell grid, rather than as a wall of text a detector would
    /// have to rediscover.
    /// </summary>
    [Fact]
    public void Map_Table_BecomesTableWithCells()
    {
        var result = CreateResult(tables:
        [
            DocumentIntelligenceModelFactory.DocumentTable(
                rowCount: 2,
                columnCount: 2,
                cells:
                [
                    Cell(0, 0, "Material"),
                    Cell(0, 1, "Strength"),
                    Cell(1, 0, "Steel"),
                    Cell(1, 1, "400"),
                ],
                boundingRegions: [Region(1, 1, 2, 3, 4)],
                spans: [DocumentIntelligenceModelFactory.DocumentSpan(0, 100)],
                caption: null,
                footnotes: null),
        ]);

        var table = Assert.IsType<IngestionDocumentTable>(
            Assert.Single(DocumentIntelligenceDocumentMapper.Map(result, "doc.pdf").EnumerateContent()));

        Assert.Equal("Material | Strength\nSteel | 400", table.Text);
        Assert.Equal("Steel", table.Cells[1, 0].Text);
    }

    /// <summary>
    /// Verifies that the paragraphs a table and a figure caption already account for are not emitted a second
    /// time, which would double the text and separate a caption from its figure.
    /// </summary>
    [Fact]
    public void Map_ParagraphsInsideTableOrCaption_AreNotDuplicated()
    {
        var result = CreateResult(
            paragraphs:
            [
                Paragraph(null, "Material", offset: 5),
                Paragraph(null, "Figure 3. The caption.", offset: 140),
                Paragraph(null, "Real body text.", offset: 300),
            ],
            tables:
            [
                DocumentIntelligenceModelFactory.DocumentTable(
                    rowCount: 1,
                    columnCount: 1,
                    cells: [Cell(0, 0, "Material")],
                    boundingRegions: [Region(1, 1, 2, 3, 4)],
                    spans: [DocumentIntelligenceModelFactory.DocumentSpan(0, 100)],
                    caption: null,
                    footnotes: null),
            ],
            figures:
            [
                DocumentIntelligenceModelFactory.DocumentFigure(
                    boundingRegions: [Region(1, 1, 5, 3, 6)],
                    spans: [DocumentIntelligenceModelFactory.DocumentSpan(120, 10)],
                    elements: null,
                    caption: DocumentIntelligenceModelFactory.DocumentCaption(
                        "Figure 3. The caption.",
                        boundingRegions: [Region(1, 1, 6, 3, 6.2f)],
                        spans: [DocumentIntelligenceModelFactory.DocumentSpan(140, 22)],
                        elements: null),
                    footnotes: null,
                    id: "1.1"),
            ]);

        var elements = DocumentIntelligenceDocumentMapper.Map(result, "doc.pdf").EnumerateContent().ToList();
        var paragraphs = elements.OfType<IngestionDocumentParagraph>().ToList();

        var paragraph = Assert.Single(paragraphs);

        Assert.Equal("Real body text.", paragraph.Text);
        Assert.Single(elements.OfType<IngestionDocumentTable>());
        Assert.Single(elements.OfType<IngestionDocumentImage>());
    }

    /// <summary>
    /// Verifies that regions reported in inches from the top-left become points from the bottom-left, which is
    /// the convention every downstream processor measures in.
    /// </summary>
    [Fact]
    public void Map_BoundingRegion_IsConvertedToPointsFromTheBottomLeft()
    {
        var result = CreateResult(paragraphs:
        [
            DocumentIntelligenceModelFactory.DocumentParagraph(
                role: null,
                content: "Body",
                boundingRegions: [Region(1, 1, 2, 3, 4)],
                spans: [DocumentIntelligenceModelFactory.DocumentSpan(0, 4)]),
        ]);

        var element = Assert.Single(DocumentIntelligenceDocumentMapper.Map(result, "doc.pdf").EnumerateContent());
        var bounds = Assert.IsType<double[]>(element.Metadata[ElementMetadataKeys.BoundingBox]);

        // The page is 11 inches tall, so a band from 2 to 4 inches below the top sits 504 to 648 points above
        // the bottom.
        Assert.Equal(72d, bounds[0], 1);
        Assert.Equal(504d, bounds[1], 1);
        Assert.Equal(216d, bounds[2], 1);
        Assert.Equal(648d, bounds[3], 1);
    }

    /// <summary>
    /// Verifies that elements come back in reading order across pages, with one section per page.
    /// </summary>
    [Fact]
    public void Map_Elements_AreOrderedByPageThenReadingOrder()
    {
        var result = CreateResult(
            pages: [Page(1), Page(2)],
            paragraphs:
            [
                Paragraph(null, "Second on page one.", offset: 50, pageNumber: 1),
                Paragraph(null, "First on page one.", offset: 10, pageNumber: 1),
                Paragraph(null, "Page two.", offset: 200, pageNumber: 2),
            ]);

        var document = DocumentIntelligenceDocumentMapper.Map(result, "doc.pdf");

        Assert.Equal(2, document.Sections.Count);
        Assert.Equal("First on page one.", document.Sections[0].Elements[0].Text);
        Assert.Equal("Second on page one.", document.Sections[0].Elements[1].Text);
        Assert.Equal(2, document.Sections[1].PageNumber);
    }

    private static AnalyzeResult CreateResult(
        IEnumerable<DocumentPage> pages = null,
        IEnumerable<DocumentParagraph> paragraphs = null,
        IEnumerable<DocumentTable> tables = null,
        IEnumerable<DocumentFigure> figures = null)
    {
        return DocumentIntelligenceModelFactory.AnalyzeResult(
            apiVersion: "2024-11-30",
            modelId: "prebuilt-layout",
            contentFormat: DocumentContentFormat.Text,
            content: string.Empty,
            pages: pages ?? [Page(1)],
            paragraphs: paragraphs ?? [],
            tables: tables ?? [],
            figures: figures ?? [],
            sections: [],
            keyValuePairs: [],
            styles: [],
            languages: [],
            documents: [],
            warnings: []);
    }

    private static DocumentPage Page(int pageNumber)
    {
        return DocumentIntelligenceModelFactory.DocumentPage(
            pageNumber: pageNumber,
            angle: 0,
            width: 8.5f,
            height: 11f,
            unit: LengthUnit.Inch,
            spans: [],
            words: [],
            selectionMarks: [],
            lines: [],
            barcodes: [],
            formulas: []);
    }

    private static DocumentParagraph Paragraph(ParagraphRole? role, string content, int offset, int pageNumber = 1)
    {
        return DocumentIntelligenceModelFactory.DocumentParagraph(
            role: role,
            content: content,
            boundingRegions: [Region(pageNumber, 1, 2, 3, 4)],
            spans: [DocumentIntelligenceModelFactory.DocumentSpan(offset, content.Length)]);
    }

    private static DocumentTableCell Cell(int row, int column, string content)
    {
        return DocumentIntelligenceModelFactory.DocumentTableCell(
            kind: null,
            rowIndex: row,
            columnIndex: column,
            rowSpan: 1,
            columnSpan: 1,
            content: content,
            boundingRegions: [],
            spans: [],
            elements: null);
    }

    /// <summary>
    /// Builds a rectangular region, in inches from the top-left corner, the way the service reports them.
    /// </summary>
    /// <param name="pageNumber">The page the region is on.</param>
    /// <param name="left">The left edge.</param>
    /// <param name="top">The top edge.</param>
    /// <param name="right">The right edge.</param>
    /// <param name="bottom">The bottom edge.</param>
    /// <returns>The region.</returns>
    private static BoundingRegion Region(int pageNumber, float left, float top, float right, float bottom)
    {
        return DocumentIntelligenceModelFactory.BoundingRegion(
            pageNumber,
            [left, top, right, top, right, bottom, left, bottom]);
    }
}
