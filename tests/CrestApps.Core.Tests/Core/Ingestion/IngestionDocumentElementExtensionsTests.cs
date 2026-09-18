using CrestApps.Core.AI.Ingestion;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.Tests.Core.Ingestion;

public sealed class IngestionDocumentElementExtensionsTests
{
    /// <summary>
    /// Verifies that a paragraph yields its own text.
    /// </summary>
    [Fact]
    public void GetSemanticText_Paragraph_ReturnsText()
    {
        var paragraph = new IngestionDocumentParagraph("markdown")
        {
            Text = "body text",
        };

        Assert.Equal("body text", paragraph.GetSemanticText());
    }

    /// <summary>
    /// Verifies that a described image yields its description.
    /// </summary>
    [Fact]
    public void GetSemanticText_ImageWithAlt_ReturnsAlt()
    {
        var image = new IngestionDocumentImage("markdown")
        {
            AlternativeText = "A stacked bar chart.",
        };

        image.Metadata[FigureMetadataKeys.Caption] = "Figure 3.";

        Assert.Equal("A stacked bar chart.", image.GetSemanticText());
    }

    /// <summary>
    /// Verifies that an image with nothing but a caption yields the caption, so a figure reaches the index
    /// even before anything has described it.
    /// </summary>
    [Fact]
    public void GetSemanticText_ImageWithCaptionOnly_ReturnsCaption()
    {
        var image = new IngestionDocumentImage("markdown");

        image.Metadata[FigureMetadataKeys.Caption] = "Figure 3. Specific heating demand.";

        Assert.Equal("Figure 3. Specific heating demand.", image.GetSemanticText());
    }

    /// <summary>
    /// Verifies that an image nothing has said anything about yields nothing. This is what keeps an
    /// undescribed figure from adding empty noise to the stored text.
    /// </summary>
    [Fact]
    public void GetSemanticText_BareImage_ReturnsNull()
    {
        var image = new IngestionDocumentImage("markdown");

        Assert.Null(image.GetSemanticText());
    }

    /// <summary>
    /// Verifies that a table with no text of its own is rendered from its cells.
    /// </summary>
    [Fact]
    public void GetSemanticText_TableWithoutText_RendersCells()
    {
        var cells = new IngestionDocumentElement[2, 2];
        cells[0, 0] = new IngestionDocumentParagraph("a") { Text = "Material" };
        cells[0, 1] = new IngestionDocumentParagraph("b") { Text = "Strength" };
        cells[1, 0] = new IngestionDocumentParagraph("c") { Text = "Steel" };
        cells[1, 1] = new IngestionDocumentParagraph("d") { Text = "400" };

        var table = new IngestionDocumentTable("markdown", cells);

        Assert.Equal("Material | Strength\nSteel | 400", table.GetSemanticText());
    }

    /// <summary>
    /// Verifies that a table keeps its own text when it has one.
    /// </summary>
    [Fact]
    public void GetSemanticText_TableWithText_ReturnsText()
    {
        var cells = new IngestionDocumentElement[1, 1];
        cells[0, 0] = new IngestionDocumentParagraph("a") { Text = "Material" };

        var table = new IngestionDocumentTable("markdown", cells)
        {
            Text = "| Material |",
        };

        Assert.Equal("| Material |", table.GetSemanticText());
    }

    /// <summary>
    /// Verifies that the figure identifier and page travel with the element.
    /// </summary>
    [Fact]
    public void GetFigureIdAndPage_ReturnStoredValues()
    {
        var image = new IngestionDocumentImage("markdown")
        {
            PageNumber = 8,
        };

        image.Metadata[FigureMetadataKeys.Id] = "report-p8-1";

        Assert.Equal("report-p8-1", image.GetFigureId());
        Assert.Equal(8, image.GetPage());
    }
}
