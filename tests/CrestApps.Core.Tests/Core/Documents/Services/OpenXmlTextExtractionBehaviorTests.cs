using CrestApps.Core.AI.Documents.OpenXml.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DataIngestion;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Presentation = DocumentFormat.OpenXml.Presentation;
using Wordprocessing = DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Tests.Core.Documents.Services;

/// <summary>
/// Verifies the exact Word and PowerPoint text extraction behavior of <see cref="OpenXmlIngestionDocumentReader"/>.
/// </summary>
public sealed class OpenXmlTextExtractionBehaviorTests
{
    private const string PowerPointMediaType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    private const string WordMediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>
    /// Verifies that empty and whitespace-only Word paragraphs do not create document sections.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WordEmptyAndWhitespaceParagraphs_ReturnsNoSections()
    {
        using var source = CreateWordDocument(
            new Wordprocessing.Paragraph(),
            new Wordprocessing.Paragraph(new Wordprocessing.Run()),
            new Wordprocessing.Paragraph(new Wordprocessing.Run(new Wordprocessing.Text(string.Empty))),
            new Wordprocessing.Paragraph(
                new Wordprocessing.Run(
                    new Wordprocessing.Text(" \t\r\n")
                    {
                        Space = SpaceProcessingModeValues.Preserve,
                    })));
        var reader = new OpenXmlIngestionDocumentReader();

        var document = await reader.ReadAsync(
            source,
            "empty-and-whitespace.docx",
            WordMediaType,
            TestContext.Current.CancellationToken);

        AssertEmptyDocument(document, "empty-and-whitespace.docx");
    }

    /// <summary>
    /// Verifies exact Word text, ordering, duplicate handling, control-element behavior, Unicode, and metadata.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WordMixedParagraphs_PreservesExactOutputAndMetadata()
    {
        const string unicodeText = "Cafe\u0301 | \uD83D\uDE00 | 漢字 | مرحبا | \uFFFD";
        const string literalControlText = "Literal\tTab\nLine";

        var duplicate = new Wordprocessing.Paragraph(
            new Wordprocessing.Run(new Wordprocessing.Text("Duplicate")));

        using var source = CreateWordDocument(
            new Wordprocessing.Paragraph(
                new Wordprocessing.Run(new Wordprocessing.Text("First"))),
            new Wordprocessing.Paragraph(
                new Wordprocessing.Run(new Wordprocessing.Text("Many")),
                new Wordprocessing.Run(
                    new Wordprocessing.Text(" ")
                    {
                        Space = SpaceProcessingModeValues.Preserve,
                    }),
                new Wordprocessing.Run(new Wordprocessing.Text("runs"))),
            new Wordprocessing.Paragraph(
                new Wordprocessing.Run(
                    new Wordprocessing.Text("Before"),
                    new Wordprocessing.TabChar(),
                    new Wordprocessing.Text("Tab"),
                    new Wordprocessing.Break(),
                    new Wordprocessing.Text("Break"))),
            new Wordprocessing.Paragraph(
                new Wordprocessing.Run(
                    new Wordprocessing.Text(literalControlText)
                    {
                        Space = SpaceProcessingModeValues.Preserve,
                    })),
            new Wordprocessing.Paragraph(
                new Wordprocessing.Run(new Wordprocessing.Text(unicodeText))),
            duplicate,
            duplicate.CloneNode(true),
            new Wordprocessing.Paragraph(
                new Wordprocessing.Run(new Wordprocessing.Text("Last"))));
        var reader = new OpenXmlIngestionDocumentReader();

        var document = await reader.ReadAsync(
            source,
            "mixed.docx",
            WordMediaType,
            TestContext.Current.CancellationToken);

        AssertExactDocument(
            document,
            "mixed.docx",
            "First",
            "Many runs",
            "BeforeTabBreak",
            literalControlText,
            unicodeText,
            "Duplicate",
            "Duplicate",
            "Last");
    }

    /// <summary>
    /// Verifies that a large Word paragraph is neither truncated nor split.
    /// </summary>
    [Fact]
    public async Task ReadAsync_WordLargeParagraph_PreservesExactText()
    {
        var largeText = string.Create(131_072, 'x', static (span, value) => span.Fill(value));
        using var source = CreateWordDocument(
            new Wordprocessing.Paragraph(
                new Wordprocessing.Run(new Wordprocessing.Text(largeText))));
        var reader = new OpenXmlIngestionDocumentReader();

        var document = await reader.ReadAsync(
            source,
            "large.docx",
            WordMediaType,
            TestContext.Current.CancellationToken);

        AssertExactDocument(document, "large.docx", largeText);
    }

    /// <summary>
    /// Verifies that a pre-canceled Word read throws before package extraction.
    /// </summary>
    [Fact]
    public async Task ReadAsync_PreCanceledWordRead_ThrowsOperationCanceledException()
    {
        using var source = CreateWordDocument(
            new Wordprocessing.Paragraph(
                new Wordprocessing.Run(new Wordprocessing.Text("Canceled"))));
        using var cancellationTokenSource = new CancellationTokenSource();
        var reader = new OpenXmlIngestionDocumentReader();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => reader.ReadAsync(source, "canceled.docx", WordMediaType, cancellationTokenSource.Token));
    }

    /// <summary>
    /// Verifies that empty and whitespace-only PowerPoint text nodes do not create document sections.
    /// </summary>
    [Fact]
    public async Task ReadAsync_PowerPointEmptyAndWhitespaceTextNodes_ReturnsNoSections()
    {
        using var source = CreatePowerPointDocument(
            CreateSlide(),
            CreateSlide(
                CreateShape(
                    2,
                    new Drawing.Paragraph(
                        new Drawing.Run(new Drawing.Text(string.Empty)),
                        new Drawing.Run(new Drawing.Text(" \t\r\n"))))));
        var reader = new OpenXmlIngestionDocumentReader();

        var document = await reader.ReadAsync(
            source,
            "empty-and-whitespace.pptx",
            PowerPointMediaType,
            TestContext.Current.CancellationToken);

        AssertEmptyDocument(document, "empty-and-whitespace.pptx");
    }

    /// <summary>
    /// Verifies exact PowerPoint text, slide and shape ordering, duplicates, breaks, Unicode, trimming, and metadata.
    /// </summary>
    [Fact]
    public async Task ReadAsync_PowerPointMixedSlides_PreservesExactOutputAndMetadata()
    {
        const string unicodeText = "Cafe\u0301 | \uD83D\uDE00 | 漢字 | مرحبا | \uFFFD";
        const string literalControlText = "Literal\tTab\nLine";

        using var source = CreatePowerPointDocument(
            CreateSlide(
                CreateShape(
                    2,
                    new Drawing.Paragraph(
                        new Drawing.Run(new Drawing.Text("Slide 1 only"))))),
            CreateSlide(
                CreateShape(
                    2,
                    new Drawing.Paragraph(
                        new Drawing.Run(new Drawing.Text(" \t")),
                        new Drawing.Run(new Drawing.Text("Slide 2 shape 1 fragment 1")),
                        new Drawing.Run(new Drawing.Text("Slide 2 shape 1 fragment 2")))),
                CreateShape(
                    3,
                    new Drawing.Paragraph(
                        new Drawing.Run(new Drawing.Text("Before break")),
                        new Drawing.Break(),
                        new Drawing.Run(new Drawing.Text("After break"))),
                    new Drawing.Paragraph(
                        new Drawing.Run(new Drawing.Text(literalControlText))))),
            CreateSlide(
                CreateShape(
                    2,
                    new Drawing.Paragraph(
                        new Drawing.Run(new Drawing.Text(string.Empty)),
                        new Drawing.Run(new Drawing.Text("\r\n\t"))))),
            CreateSlide(
                CreateShape(
                    2,
                    new Drawing.Paragraph(
                        new Drawing.Run(new Drawing.Text("Duplicate")),
                        new Drawing.Run(new Drawing.Text("Duplicate")))),
                CreateShape(
                    3,
                    new Drawing.Paragraph(
                        new Drawing.Run(new Drawing.Text(unicodeText)),
                        new Drawing.Run(new Drawing.Text("Trailing  "))))));
        var reader = new OpenXmlIngestionDocumentReader();

        var document = await reader.ReadAsync(
            source,
            "mixed.pptx",
            PowerPointMediaType,
            TestContext.Current.CancellationToken);

        AssertExactDocument(
            document,
            "mixed.pptx",
            "Slide 1 only",
            string.Join(
                Environment.NewLine,
                "Slide 2 shape 1 fragment 1",
                "Slide 2 shape 1 fragment 2",
                "Before break",
                "After break",
                literalControlText),
            string.Join(
                Environment.NewLine,
                "Duplicate",
                "Duplicate",
                unicodeText,
                "Trailing"));
    }

    /// <summary>
    /// Verifies that a large PowerPoint text fragment is neither truncated nor split.
    /// </summary>
    [Fact]
    public async Task ReadAsync_PowerPointLargeTextFragment_PreservesExactText()
    {
        var largeText = string.Create(131_072, 'x', static (span, value) => span.Fill(value));
        using var source = CreatePowerPointDocument(
            CreateSlide(
                CreateShape(
                    2,
                    new Drawing.Paragraph(
                        new Drawing.Run(new Drawing.Text(largeText))))));
        var reader = new OpenXmlIngestionDocumentReader();

        var document = await reader.ReadAsync(
            source,
            "large.pptx",
            PowerPointMediaType,
            TestContext.Current.CancellationToken);

        AssertExactDocument(document, "large.pptx", largeText);
    }

    /// <summary>
    /// Verifies that a pre-canceled PowerPoint read throws before package extraction.
    /// </summary>
    [Fact]
    public async Task ReadAsync_PreCanceledPowerPointRead_ThrowsOperationCanceledException()
    {
        using var source = CreatePowerPointDocument(
            CreateSlide(
                CreateShape(
                    2,
                    new Drawing.Paragraph(
                        new Drawing.Run(new Drawing.Text("Canceled"))))));
        using var cancellationTokenSource = new CancellationTokenSource();
        var reader = new OpenXmlIngestionDocumentReader();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => reader.ReadAsync(source, "canceled.pptx", PowerPointMediaType, cancellationTokenSource.Token));
    }

    /// <summary>
    /// Verifies that invalid Word and PowerPoint packages preserve the Open XML SDK exception behavior.
    /// </summary>
    /// <param name="identifier">The document identifier used for the read.</param>
    /// <param name="mediaType">The media type selecting the Open XML package provider.</param>
    [Theory]
    [InlineData("invalid.docx", WordMediaType)]
    [InlineData("invalid.pptx", PowerPointMediaType)]
    public async Task ReadAsync_InvalidWordOrPowerPointPackage_ThrowsFileFormatException(
        string identifier,
        string mediaType)
    {
        using var source = new MemoryStream([0x00, 0x01, 0x02, 0xFF, 0x50, 0x4B]);
        var reader = new OpenXmlIngestionDocumentReader();

        await Assert.ThrowsAsync<FileFormatException>(
            () => reader.ReadAsync(source, identifier, mediaType, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Creates a Word document from the supplied body elements.
    /// </summary>
    /// <param name="bodyElements">The body elements to serialize in order.</param>
    /// <returns>The serialized document stream positioned at the beginning.</returns>
    private static MemoryStream CreateWordDocument(params OpenXmlElement[] bodyElements)
    {
        var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            var body = new Wordprocessing.Body();
            body.Append(bodyElements);
            mainPart.Document = new Wordprocessing.Document(body);
            mainPart.Document.Save();
        }

        stream.Position = 0;

        return stream;
    }

    /// <summary>
    /// Creates a PowerPoint document from the supplied slides.
    /// </summary>
    /// <param name="slides">The slides to serialize in relationship order.</param>
    /// <returns>The serialized presentation stream positioned at the beginning.</returns>
    private static MemoryStream CreatePowerPointDocument(params Presentation.Slide[] slides)
    {
        var stream = new MemoryStream();

        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = document.AddPresentationPart();
            presentationPart.Presentation = new Presentation.Presentation();
            var slideIdList = presentationPart.Presentation.AppendChild(new Presentation.SlideIdList());
            uint slideId = 256;

            foreach (var slide in slides)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = slide;
                slidePart.Slide.Save();
                slideIdList.AppendChild(new Presentation.SlideId
                {
                    Id = slideId++,
                    RelationshipId = presentationPart.GetIdOfPart(slidePart),
                });
            }

            presentationPart.Presentation.Save();
        }

        stream.Position = 0;

        return stream;
    }

    /// <summary>
    /// Creates a slide containing the supplied shapes.
    /// </summary>
    /// <param name="shapes">The shapes to append in document order.</param>
    /// <returns>The synthetic slide.</returns>
    private static Presentation.Slide CreateSlide(params Presentation.Shape[] shapes)
    {
        var shapeTree = new Presentation.ShapeTree(
            new Presentation.NonVisualGroupShapeProperties(
                new Presentation.NonVisualDrawingProperties
                {
                    Id = 1,
                    Name = string.Empty,
                },
                new Presentation.NonVisualGroupShapeDrawingProperties(),
                new Presentation.ApplicationNonVisualDrawingProperties()),
            new Presentation.GroupShapeProperties(new Drawing.TransformGroup()));
        shapeTree.Append(shapes);

        return new Presentation.Slide(new Presentation.CommonSlideData(shapeTree));
    }

    /// <summary>
    /// Creates a text-bearing PowerPoint shape.
    /// </summary>
    /// <param name="id">The shape identifier within its slide.</param>
    /// <param name="paragraphs">The drawing paragraphs to append in order.</param>
    /// <returns>The synthetic shape.</returns>
    private static Presentation.Shape CreateShape(uint id, params Drawing.Paragraph[] paragraphs)
    {
        var textBody = new Presentation.TextBody(
            new Drawing.BodyProperties(),
            new Drawing.ListStyle());
        textBody.Append(paragraphs);

        return new Presentation.Shape(
            new Presentation.NonVisualShapeProperties(
                new Presentation.NonVisualDrawingProperties
                {
                    Id = id,
                    Name = $"Shape {id}",
                },
                new Presentation.NonVisualShapeDrawingProperties(),
                new Presentation.ApplicationNonVisualDrawingProperties()),
            new Presentation.ShapeProperties(),
            textBody);
    }

    /// <summary>
    /// Asserts a document with no extracted sections or content.
    /// </summary>
    /// <param name="document">The document to inspect.</param>
    /// <param name="identifier">The expected document identifier.</param>
    private static void AssertEmptyDocument(IngestionDocument document, string identifier)
    {
        Assert.Equal(identifier, document.Identifier);
        Assert.Empty(document.Sections);
        Assert.Empty(document.EnumerateContent());
    }

    /// <summary>
    /// Asserts exact document structure, paragraph text, markdown, page metadata, and custom metadata.
    /// </summary>
    /// <param name="document">The document to inspect.</param>
    /// <param name="identifier">The expected document identifier.</param>
    /// <param name="expectedTexts">The exact paragraph texts in extraction order.</param>
    private static void AssertExactDocument(
        IngestionDocument document,
        string identifier,
        params string[] expectedTexts)
    {
        Assert.Equal(identifier, document.Identifier);

        var section = Assert.Single(document.Sections);
        Assert.Null(section.Text);
        Assert.Null(section.PageNumber);
        Assert.False(section.HasMetadata);
        Assert.Empty(section.Metadata);
        Assert.Equal(string.Join(Environment.NewLine, expectedTexts), section.GetMarkdown());
        Assert.Equal(expectedTexts.Length, section.Elements.Count);
        Assert.Equal(expectedTexts.Length, document.EnumerateContent().Count());

        for (var index = 0; index < expectedTexts.Length; index++)
        {
            var paragraph = Assert.IsType<IngestionDocumentParagraph>(section.Elements[index]);
            Assert.Equal(expectedTexts[index], paragraph.Text);
            Assert.Equal(expectedTexts[index], paragraph.GetMarkdown());
            Assert.Null(paragraph.PageNumber);
            Assert.False(paragraph.HasMetadata);
            Assert.Empty(paragraph.Metadata);
        }
    }
}
