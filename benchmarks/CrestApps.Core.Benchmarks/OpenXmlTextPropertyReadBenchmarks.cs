using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DataIngestion;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Presentation = DocumentFormat.OpenXml.Presentation;
using Wordprocessing = DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Compares repeated and single-read Word paragraph text extraction using in-memory documents.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class OpenXmlWordTextPropertyReadBenchmarks
{
    private const string WordMediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private readonly OpenXmlIngestionDocumentReader _reader = new();
    private byte[] _document;

    /// <summary>
    /// Gets or sets the number of Word paragraphs in the synthetic document.
    /// </summary>
    [Params(1_000, 10_000)]
    public int ElementCount { get; set; }

    /// <summary>
    /// Gets or sets the number of runs in each Word paragraph.
    /// </summary>
    [Params(1, 8)]
    public int RunsPerElement { get; set; }

    /// <summary>
    /// Creates the synthetic Word document and verifies exact legacy/current output equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _document = CreateDocument(ElementCount, RunsPerElement);

        var legacy = ReadLegacy();
        var current = ReadCurrent();
        OpenXmlBenchmarkEquivalence.AssertEquivalent(legacy, current);
        OpenXmlBenchmarkEquivalence.AssertElementCount(current, ElementCount);
    }

    /// <summary>
    /// Reads the document with the original repeated <see cref="OpenXmlElement.InnerText"/> accesses.
    /// </summary>
    /// <returns>The extracted ingestion document.</returns>
    [Benchmark(Baseline = true)]
    public IngestionDocument ReadLegacy()
    {
        using var stream = new MemoryStream(_document, writable: false);

        return ExtractLegacy(stream);
    }

    /// <summary>
    /// Reads the document with the current production implementation.
    /// </summary>
    /// <returns>The extracted ingestion document.</returns>
    [Benchmark]
    public IngestionDocument ReadCurrent()
    {
        using var stream = new MemoryStream(_document, writable: false);

        return _reader.ReadAsync(stream, "benchmark.docx", WordMediaType).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Creates a synthetic Word document entirely in memory.
    /// </summary>
    /// <param name="elementCount">The number of paragraphs to create.</param>
    /// <param name="runsPerElement">The number of text runs in each paragraph.</param>
    /// <returns>The serialized Word document.</returns>
    private static byte[] CreateDocument(int elementCount, int runsPerElement)
    {
        using var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            var body = new Wordprocessing.Body();

            for (var elementIndex = 0; elementIndex < elementCount; elementIndex++)
            {
                var paragraph = new Wordprocessing.Paragraph();

                for (var runIndex = 0; runIndex < runsPerElement; runIndex++)
                {
                    paragraph.AppendChild(
                        new Wordprocessing.Run(
                            new Wordprocessing.Text(
                                $"Paragraph {elementIndex:D5} run {runIndex:D2} payload.")));
                }

                body.AppendChild(paragraph);
            }

            mainPart.Document = new Wordprocessing.Document(body);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Reproduces the original Word extraction implementation.
    /// </summary>
    /// <param name="stream">The Word document stream.</param>
    /// <returns>The extracted ingestion document.</returns>
    private static IngestionDocument ExtractLegacy(Stream stream)
    {
        using var document = WordprocessingDocument.Open(stream, false);
        var result = new IngestionDocument("benchmark.docx");
        var body = document.MainDocumentPart?.Document?.Body;

        if (body == null)
        {
            return result;
        }

        var section = new IngestionDocumentSection();

        foreach (var paragraph in body.Descendants<Wordprocessing.Paragraph>())
        {
            CancellationToken.None.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(paragraph.InnerText))
            {
                section.Elements.Add(new IngestionDocumentParagraph(paragraph.InnerText)
                {
                    Text = paragraph.InnerText,
                });
            }
        }

        if (section.Elements.Count > 0)
        {
            result.Sections.Add(section);
        }

        return result;
    }
}

/// <summary>
/// Compares repeated and single-read PowerPoint drawing text extraction using in-memory presentations.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class OpenXmlPowerPointTextPropertyReadBenchmarks
{
    private const string PowerPointMediaType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private readonly OpenXmlIngestionDocumentReader _reader = new();
    private byte[] _document;

    /// <summary>
    /// Gets or sets the number of slides in the synthetic presentation.
    /// </summary>
    [Params(1_000, 10_000)]
    public int ElementCount { get; set; }

    /// <summary>
    /// Gets or sets the number of drawing text fragments in each slide.
    /// </summary>
    [Params(1, 8)]
    public int TextFragmentsPerElement { get; set; }

    /// <summary>
    /// Creates the synthetic presentation and verifies exact legacy/candidate/production output equivalence.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _document = CreateDocument(ElementCount, TextFragmentsPerElement);

        var legacy = ReadLegacy();
        var candidate = ReadCurrentCandidate();
        var production = ReadProduction();
        OpenXmlBenchmarkEquivalence.AssertEquivalent(legacy, candidate);
        OpenXmlBenchmarkEquivalence.AssertEquivalent(legacy, production);
        OpenXmlBenchmarkEquivalence.AssertElementCount(candidate, ElementCount);
    }

    /// <summary>
    /// Reads the presentation with the original repeated drawing <see cref="OpenXmlLeafTextElement.Text"/> accesses.
    /// </summary>
    /// <returns>The extracted ingestion document.</returns>
    [Benchmark(Baseline = true)]
    public IngestionDocument ReadLegacy()
    {
        using var stream = new MemoryStream(_document, writable: false);

        return ExtractLegacy(stream);
    }

    /// <summary>
    /// Reads the presentation with one drawing text property access per text element.
    /// </summary>
    /// <returns>The extracted ingestion document.</returns>
    [Benchmark]
    public IngestionDocument ReadCurrentCandidate()
    {
        using var stream = new MemoryStream(_document, writable: false);

        return ExtractSingleReadCandidate(stream);
    }

    /// <summary>
    /// Reads the presentation with the current production implementation for setup-only equivalence verification.
    /// </summary>
    /// <returns>The extracted ingestion document.</returns>
    private IngestionDocument ReadProduction()
    {
        using var stream = new MemoryStream(_document, writable: false);

        return _reader.ReadAsync(stream, "benchmark.pptx", PowerPointMediaType).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Creates a synthetic PowerPoint presentation entirely in memory.
    /// </summary>
    /// <param name="elementCount">The number of slides to create.</param>
    /// <param name="textFragmentsPerElement">The number of drawing text fragments in each slide.</param>
    /// <returns>The serialized PowerPoint presentation.</returns>
    private static byte[] CreateDocument(int elementCount, int textFragmentsPerElement)
    {
        using var stream = new MemoryStream();

        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = document.AddPresentationPart();
            presentationPart.Presentation = new Presentation.Presentation();
            var slideIdList = presentationPart.Presentation.AppendChild(new Presentation.SlideIdList());
            uint slideId = 256;

            for (var elementIndex = 0; elementIndex < elementCount; elementIndex++)
            {
                var paragraph = new Drawing.Paragraph();

                for (var fragmentIndex = 0; fragmentIndex < textFragmentsPerElement; fragmentIndex++)
                {
                    paragraph.AppendChild(
                        new Drawing.Run(
                            new Drawing.Text(
                                $"Slide {elementIndex:D5} fragment {fragmentIndex:D2} payload.")));
                }

                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = CreateSlide(paragraph);
                slidePart.Slide.Save();
                slideIdList.AppendChild(new Presentation.SlideId
                {
                    Id = slideId++,
                    RelationshipId = presentationPart.GetIdOfPart(slidePart),
                });
            }

            presentationPart.Presentation.Save();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Creates a minimal synthetic slide containing one text-bearing shape.
    /// </summary>
    /// <param name="paragraph">The drawing paragraph to place on the slide.</param>
    /// <returns>The synthetic slide.</returns>
    private static Presentation.Slide CreateSlide(Drawing.Paragraph paragraph)
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
        var textBody = new Presentation.TextBody(
            new Drawing.BodyProperties(),
            new Drawing.ListStyle(),
            paragraph);
        shapeTree.AppendChild(
            new Presentation.Shape(
                new Presentation.NonVisualShapeProperties(
                    new Presentation.NonVisualDrawingProperties
                    {
                        Id = 2,
                        Name = "Text",
                    },
                    new Presentation.NonVisualShapeDrawingProperties(),
                    new Presentation.ApplicationNonVisualDrawingProperties()),
                new Presentation.ShapeProperties(),
                textBody));

        return new Presentation.Slide(new Presentation.CommonSlideData(shapeTree));
    }

    /// <summary>
    /// Reproduces the original PowerPoint extraction implementation.
    /// </summary>
    /// <param name="stream">The PowerPoint presentation stream.</param>
    /// <returns>The extracted ingestion document.</returns>
    private static IngestionDocument ExtractLegacy(Stream stream)
    {
        using var document = PresentationDocument.Open(stream, false);
        var result = new IngestionDocument("benchmark.pptx");
        var presentation = document.PresentationPart;

        if (presentation == null)
        {
            return result;
        }

        var section = new IngestionDocumentSection();
        var builder = new StringBuilder();

        foreach (var slide in presentation.SlideParts)
        {
            CancellationToken.None.ThrowIfCancellationRequested();
            builder.Clear();

            foreach (var text in slide.Slide.Descendants<Drawing.Text>())
            {
                if (!string.IsNullOrWhiteSpace(text.Text))
                {
                    builder.AppendLine(text.Text);
                }
            }

            if (builder.Length > 0)
            {
                var slideText = builder.ToString().TrimEnd();
                section.Elements.Add(new IngestionDocumentParagraph(slideText)
                {
                    Text = slideText,
                });
            }
        }

        if (section.Elements.Count > 0)
        {
            result.Sections.Add(section);
        }

        return result;
    }

    /// <summary>
    /// Reproduces the candidate PowerPoint extraction with one property read per drawing text element.
    /// </summary>
    /// <param name="stream">The PowerPoint presentation stream.</param>
    /// <returns>The extracted ingestion document.</returns>
    private static IngestionDocument ExtractSingleReadCandidate(Stream stream)
    {
        using var document = PresentationDocument.Open(stream, false);
        var result = new IngestionDocument("benchmark.pptx");
        var presentation = document.PresentationPart;

        if (presentation == null)
        {
            return result;
        }

        var section = new IngestionDocumentSection();
        var builder = new StringBuilder();

        foreach (var slide in presentation.SlideParts)
        {
            CancellationToken.None.ThrowIfCancellationRequested();
            builder.Clear();

            foreach (var text in slide.Slide.Descendants<Drawing.Text>())
            {
                var value = text.Text;

                if (!string.IsNullOrWhiteSpace(value))
                {
                    builder.AppendLine(value);
                }
            }

            if (builder.Length > 0)
            {
                var slideText = builder.ToString().TrimEnd();
                section.Elements.Add(new IngestionDocumentParagraph(slideText)
                {
                    Text = slideText,
                });
            }
        }

        if (section.Elements.Count > 0)
        {
            result.Sections.Add(section);
        }

        return result;
    }
}

/// <summary>
/// Provides exact output checks shared by the Open XML text property benchmarks.
/// </summary>
internal static class OpenXmlBenchmarkEquivalence
{
    /// <summary>
    /// Verifies that two ingestion documents have exactly equivalent observable structure and content.
    /// </summary>
    /// <param name="expected">The expected ingestion document.</param>
    /// <param name="actual">The ingestion document to compare.</param>
    public static void AssertEquivalent(IngestionDocument expected, IngestionDocument actual)
    {
        if (!string.Equals(expected.Identifier, actual.Identifier, StringComparison.Ordinal) ||
            expected.Sections.Count != actual.Sections.Count)
        {
            throw new InvalidOperationException("Open XML benchmark documents differ at the document level.");
        }

        for (var sectionIndex = 0; sectionIndex < expected.Sections.Count; sectionIndex++)
        {
            AssertEquivalent(expected.Sections[sectionIndex], actual.Sections[sectionIndex]);
        }
    }

    /// <summary>
    /// Verifies the exact number of extracted ingestion elements.
    /// </summary>
    /// <param name="document">The ingestion document to inspect.</param>
    /// <param name="expectedCount">The expected element count.</param>
    public static void AssertElementCount(IngestionDocument document, int expectedCount)
    {
        var actualCount = document.Sections.Sum(section => section.Elements.Count);

        if (actualCount != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} Open XML benchmark elements but extracted {actualCount}.");
        }
    }

    /// <summary>
    /// Verifies that two ingestion document elements have exactly equivalent observable values.
    /// </summary>
    /// <param name="expected">The expected element.</param>
    /// <param name="actual">The element to compare.</param>
    private static void AssertEquivalent(
        IngestionDocumentElement expected,
        IngestionDocumentElement actual)
    {
        if (expected.GetType() != actual.GetType() ||
            !string.Equals(expected.Text, actual.Text, StringComparison.Ordinal) ||
            !string.Equals(expected.GetMarkdown(), actual.GetMarkdown(), StringComparison.Ordinal) ||
            expected.PageNumber != actual.PageNumber ||
            expected.HasMetadata != actual.HasMetadata ||
            expected.Metadata.Count != actual.Metadata.Count)
        {
            throw new InvalidOperationException("Open XML benchmark documents differ at the element level.");
        }

        foreach (var metadata in expected.Metadata)
        {
            if (!actual.Metadata.TryGetValue(metadata.Key, out var actualValue) ||
                !Equals(metadata.Value, actualValue))
            {
                throw new InvalidOperationException("Open XML benchmark documents differ in metadata.");
            }
        }

        if (expected is not IngestionDocumentSection expectedSection ||
            actual is not IngestionDocumentSection actualSection)
        {
            return;
        }

        if (expectedSection.Elements.Count != actualSection.Elements.Count)
        {
            throw new InvalidOperationException("Open XML benchmark sections contain different element counts.");
        }

        for (var elementIndex = 0; elementIndex < expectedSection.Elements.Count; elementIndex++)
        {
            AssertEquivalent(expectedSection.Elements[elementIndex], actualSection.Elements[elementIndex]);
        }
    }
}
