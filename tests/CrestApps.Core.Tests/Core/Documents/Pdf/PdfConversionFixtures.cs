using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using P = DocumentFormat.OpenXml.Presentation;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

/// <summary>
/// Builds the Word and PowerPoint files the conversion tests convert, in memory.
/// </summary>
internal static class PdfConversionFixtures
{
    private const long InchEmu = 914400;

    /// <summary>
    /// Builds a Word document with a heading, formatted text and a link, a bulleted and a numbered list, a
    /// table, a page break, a second-level heading and a picture.
    /// </summary>
    /// <returns>The .docx file.</returns>
    public static byte[] WordDocument()
    {
        using var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var numbering = main.AddNewPart<NumberingDefinitionsPart>();

            numbering.Numbering = new Numbering(
                new AbstractNum(new Level(new NumberingFormat { Val = NumberFormatValues.Bullet }) { LevelIndex = 0 }) { AbstractNumberId = 1 },
                new AbstractNum(new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }) { LevelIndex = 0 }) { AbstractNumberId = 2 },
                new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 },
                new NumberingInstance(new AbstractNumId { Val = 2 }) { NumberID = 2 });

            var link = main.AddHyperlinkRelationship(new Uri("https://example.com/report"), isExternal: true);
            var image = main.AddImagePart(ImagePartType.Png);

            using (var png = new MemoryStream(PdfTestImages.RedSquarePng(32)))
            {
                image.FeedData(png);
            }

            main.Document = new Document(new Body(
                Styled("Heading1", new Run(new Text("Quarterly review"))),
                new Paragraph(
                    new Run(new Text("Revenue grew ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Run(new RunProperties(new Bold()), new Text("12%")),
                    new Run(new Text(" — see ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Hyperlink(new Run(new Text("the report"))) { Id = link.Id },
                    new Run(new Text(" for details (a*b stays literal).") { Space = SpaceProcessingModeValues.Preserve })),
                ListItem("North", 1),
                ListItem("South", 1),
                ListItem("Plan", 2),
                ListItem("Execute", 2),
                new Table(
                    Row("Region", "Revenue"),
                    Row("North", "1,200"),
                    Row("South", "950")),
                new Paragraph(new Run(new Break { Type = BreakValues.Page })),
                Styled("Heading2", new Run(new Text("Appendix"))),
                new Paragraph(new Run(Picture(main.GetIdOfPart(image))))));
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Builds a deck of three slides: a titled one with bullets, a table, a picture and speaker notes; a
    /// hidden one; and one without a title.
    /// </summary>
    /// <returns>The .pptx file.</returns>
    public static byte[] Presentation()
    {
        using var stream = new MemoryStream();

        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentation = document.AddPresentationPart();
            presentation.Presentation = new P.Presentation();

            var slides = presentation.Presentation.AppendChild(new P.SlideIdList());
            uint id = 256;

            var first = presentation.AddNewPart<SlidePart>();
            var image = first.AddImagePart(ImagePartType.Png);

            using (var png = new MemoryStream(PdfTestImages.RedSquarePng(32)))
            {
                image.FeedData(png);
            }

            first.Slide = Slide(
                TextShape(2, P.PlaceholderValues.Title, 0, Paragraph("Roadmap")),
                TextShape(3, P.PlaceholderValues.Body, InchEmu, Paragraph("Launch in March"), Paragraph("Hire two engineers"), Paragraph("One in support", level: 1)),
                TableFrame(4, 3 * InchEmu),
                PictureShape(5, first.GetIdOfPart(image), 4 * InchEmu));

            var notes = first.AddNewPart<NotesSlidePart>();
            notes.NotesSlide = new P.NotesSlide(new P.CommonSlideData(new P.ShapeTree(
                GroupProperties(),
                TextShape(2, P.PlaceholderValues.Body, 0, Paragraph("Mention the budget")))));

            slides.AppendChild(new P.SlideId { Id = id++, RelationshipId = presentation.GetIdOfPart(first) });

            var hidden = presentation.AddNewPart<SlidePart>();
            hidden.Slide = Slide(TextShape(2, P.PlaceholderValues.Title, 0, Paragraph("Draft ideas")));
            hidden.Slide.Show = false;
            slides.AppendChild(new P.SlideId { Id = id++, RelationshipId = presentation.GetIdOfPart(hidden) });

            var untitled = presentation.AddNewPart<SlidePart>();
            untitled.Slide = Slide(TextShape(2, null, 0, Paragraph("Questions welcome")));
            slides.AppendChild(new P.SlideId { Id = id, RelationshipId = presentation.GetIdOfPart(untitled) });
        }

        return stream.ToArray();
    }

    private static Paragraph Styled(string style, params OpenXmlElement[] content)
    {
        var paragraph = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = style }));

        paragraph.Append(content);

        return paragraph;
    }

    private static Paragraph ListItem(string text, int numberId)
    {
        return new Paragraph(
            new ParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = numberId })),
            new Run(new Text(text)));
    }

    private static TableRow Row(params string[] cells)
    {
        return new TableRow(cells.Select(cell => new TableCell(new Paragraph(new Run(new Text(cell))))));
    }

    private static DocumentFormat.OpenXml.Wordprocessing.Drawing Picture(string relationshipId)
    {
        return new DocumentFormat.OpenXml.Wordprocessing.Drawing(new DW.Inline(
            new DW.Extent { Cx = InchEmu, Cy = InchEmu },
            new DW.DocProperties { Id = 1U, Name = "Logo", Description = "Contoso logo" },
            new A.Graphic(new A.GraphicData(
                new Pic.Picture(
                    new Pic.NonVisualPictureProperties(new Pic.NonVisualDrawingProperties { Id = 0U, Name = "logo.png" }, new Pic.NonVisualPictureDrawingProperties()),
                    new Pic.BlipFill(new A.Blip { Embed = relationshipId }, new A.Stretch(new A.FillRectangle())),
                    new Pic.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = InchEmu, Cy = InchEmu }), new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle })))
            {
                Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture",
            })));
    }

    private static P.Slide Slide(params OpenXmlElement[] shapes)
    {
        var tree = new P.ShapeTree(GroupProperties(), new P.GroupShapeProperties(new A.TransformGroup()));

        tree.Append(shapes);

        return new P.Slide(new P.CommonSlideData(tree));
    }

    private static P.NonVisualGroupShapeProperties GroupProperties()
    {
        return new P.NonVisualGroupShapeProperties(
            new P.NonVisualDrawingProperties { Id = 1, Name = string.Empty },
            new P.NonVisualGroupShapeDrawingProperties(),
            new P.ApplicationNonVisualDrawingProperties());
    }

    private static P.Shape TextShape(uint id, P.PlaceholderValues? placeholder, long top, params A.Paragraph[] paragraphs)
    {
        var application = new P.ApplicationNonVisualDrawingProperties();

        if (placeholder is { } type)
        {
            application.Append(new P.PlaceholderShape { Type = type });
        }

        var body = new P.TextBody(new A.BodyProperties(), new A.ListStyle());

        body.Append(paragraphs);

        return new P.Shape(
            new P.NonVisualShapeProperties(new P.NonVisualDrawingProperties { Id = id, Name = "Shape " + id }, new P.NonVisualShapeDrawingProperties(), application),
            new P.ShapeProperties(new A.Transform2D(new A.Offset { X = InchEmu, Y = top }, new A.Extents { Cx = 6 * InchEmu, Cy = InchEmu })),
            body);
    }

    private static A.Paragraph Paragraph(string text, int level = 0)
    {
        return new A.Paragraph(
            new A.ParagraphProperties { Level = level },
            new A.Run(new A.RunProperties { Language = "en-US" }, new A.Text(text)));
    }

    private static P.GraphicFrame TableFrame(uint id, long top)
    {
        static A.TableRow Row(params string[] cells)
        {
            return new A.TableRow(cells.Select(cell => new A.TableCell(new A.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.Run(new A.Text(cell)))), new A.TableCellProperties()))) { Height = 370840 };
        }

        return new P.GraphicFrame(
            new P.NonVisualGraphicFrameProperties(new P.NonVisualDrawingProperties { Id = id, Name = "Table" }, new P.NonVisualGraphicFrameDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
            new P.Transform(new A.Offset { X = InchEmu, Y = top }, new A.Extents { Cx = 6 * InchEmu, Cy = InchEmu }),
            new A.Graphic(new A.GraphicData(new A.Table(
                new A.TableGrid(new A.GridColumn { Width = 3 * InchEmu }, new A.GridColumn { Width = 3 * InchEmu }),
                Row("Quarter", "Goal"),
                Row("Q1", "Launch")))
            {
                Uri = "http://schemas.openxmlformats.org/drawingml/2006/table",
            }));
    }

    private static P.Picture PictureShape(uint id, string relationshipId, long top)
    {
        return new P.Picture(
            new P.NonVisualPictureProperties(new P.NonVisualDrawingProperties { Id = id, Name = "Photo", Description = "Team photo" }, new P.NonVisualPictureDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
            new P.BlipFill(new A.Blip { Embed = relationshipId }, new A.Stretch(new A.FillRectangle())),
            new P.ShapeProperties(new A.Transform2D(new A.Offset { X = InchEmu, Y = top }, new A.Extents { Cx = 2 * InchEmu, Cy = 2 * InchEmu })));
    }
}
