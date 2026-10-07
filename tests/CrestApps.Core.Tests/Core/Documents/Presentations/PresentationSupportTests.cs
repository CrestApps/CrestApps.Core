using System.Text.Json.Nodes;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.OpenXml.Presentations;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using CrestApps.Core.AI.Documents.Presentations.Rendering;
using CrestApps.Core.AI.Documents.Tools.Presentations;
using CrestApps.Core.AI.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Documents.Presentations;

/// <summary>
/// Verifies the pieces the presentation tools are built from: the Markdown deck writer, the slide renderer,
/// argument reading, colours, the workspace and the quality checks.
/// </summary>
public sealed class PresentationSupportTests
{
    private readonly OpenXmlPresentationEngine _engine = new(TimeProvider.System, NullLogger<OpenXmlPresentationEngine>.Instance);

    /// <summary>
    /// Markdown becomes a title slide and one slide per heading, with bullets, notes and tables.
    /// </summary>
    [Fact]
    public async Task Writer_TurnsMarkdownIntoSlides()
    {
        var writer = new PresentationGeneratedFileWriter(_engine);
        var content = new GeneratedFileContent
        {
            Text = """
                # Launch plan
                Everything we need to ship

                ## Why now
                - The market is ready
                - We have the team
                > Notes: Open with the customer quote.

                ## Budget
                | Item | Cost |
                | --- | --- |
                | People | 100 |
                | Tools | 20 |
                """,
        };

        using var output = new MemoryStream();
        await writer.WriteAsync(content, output, TestContext.Current.CancellationToken);

        var package = output.ToArray();
        var model = await _engine.ReadAsync(package, PresentationReadOptions.TextOnly, TestContext.Current.CancellationToken);

        Assert.Empty(await _engine.ValidateAsync(package, TestContext.Current.CancellationToken));
        Assert.Equal(["Launch plan", "Why now", "Budget"], model.Slides.Select(slide => slide.Title));
        Assert.Contains(model.Slides[0].AllElements(), element => element.Text?.PlainText == "Everything we need to ship");
        Assert.Equal("Open with the customer quote.", model.Slides[1].Notes);
        Assert.Equal(["People", "100"], model.Slides[2].AllElements().Single(element => element.Table is not null).Table.ToText()[1]);
    }

    /// <summary>
    /// A PowerPoint template is recognised and its slides' text is read for search, like a deck's.
    /// </summary>
    [Fact]
    public async Task Ingestion_ReadsTemplates()
    {
        var package = await _engine.CreateAsync(new PresentationCreateOptions(), TestContext.Current.CancellationToken);
        var result = await _engine.EditAsync(package, [new AddSlideEdit { Title = "Template sample slide" }], new PresentationEditContext(), TestContext.Current.CancellationToken);

        using var stream = new MemoryStream();
        stream.Write(result.Package);
        stream.Position = 0;

        using (var document = DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(stream, true))
        {
            document.ChangeDocumentType(DocumentFormat.OpenXml.PresentationDocumentType.Template);
        }

        stream.Position = 0;

        var mediaType = MediaTypeHelper.InferMediaType(".potx");
        var read = await new OpenXmlIngestionDocumentReader().ReadAsync(stream, "brand.potx", mediaType, TestContext.Current.CancellationToken);

        Assert.Equal("application/vnd.openxmlformats-officedocument.presentationml.template", mediaType);
        Assert.Contains(read.Sections.SelectMany(section => section.Elements), element => element.Text.Contains("Template sample slide", StringComparison.Ordinal));
    }

    /// <summary>
    /// The SVG of a slide escapes its text, carries no script, and names the slide for screen readers.
    /// </summary>
    [Fact]
    public async Task Svg_EscapesTextAndCarriesNoScript()
    {
        var package = await _engine.CreateAsync(new PresentationCreateOptions(), TestContext.Current.CancellationToken);
        var result = await _engine.EditAsync(
            package,
            [new AddSlideEdit { Title = "<script>alert(1)</script> & more", Body = [PresentationParagraphSpec.FromText("a < b")] }],
            new PresentationEditContext(),
            TestContext.Current.CancellationToken);

        var model = await _engine.ReadAsync(result.Package, new PresentationReadOptions(), TestContext.Current.CancellationToken);
        var svg = SlideSvgWriter.Write(SlideDrawingBuilder.Build(model, model.Slides[0]), 960);

        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", svg, StringComparison.Ordinal);
        Assert.Contains("a &lt; b", svg, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Slide 1: ", svg, StringComparison.Ordinal);
        Assert.Contains("width=\"960\"", svg, StringComparison.Ordinal);
    }

    /// <summary>
    /// Content the renderer cannot draw is shown as a labelled box, and says so.
    /// </summary>
    [Fact]
    public void Drawing_LabelsUnsupportedContent()
    {
        var model = new PresentationModel { SlideWidth = PresentationUnits.WideSlideWidth, SlideHeight = PresentationUnits.WideSlideHeight };
        var slide = new PresentationSlide
        {
            Number = 1,
            Elements =
            [
                new PresentationElement
                {
                    Id = 5,
                    Name = "SmartArt 5",
                    Kind = PresentationElementKind.Diagram,
                    Bounds = new PresentationBounds(914_400, 914_400, 4_000_000, 2_000_000),
                    UnsupportedDescription = "a SmartArt diagram",
                    FallbackText = "Plan Build Launch",
                },
            ],
        };

        model.Slides.Add(slide);

        var drawing = SlideDrawingBuilder.Build(model, slide);

        Assert.NotEmpty(drawing.Placeholders);
        Assert.Contains(drawing.Items.OfType<SlideTextDrawing>().SelectMany(text => text.Lines).SelectMany(line => line.Runs), run => run.Text.Contains("SmartArt", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Lengths are read in points, inches, centimetres and percentages of the slide.
    /// </summary>
    /// <param name="text">The length as written.</param>
    /// <param name="reference">The slide dimension a percentage is taken of.</param>
    /// <param name="expected">The length in EMUs.</param>
    [Theory]
    [InlineData("72", 0, 914_400)]
    [InlineData("1in", 0, 914_400)]
    [InlineData("2.54cm", 0, 914_400)]
    [InlineData("50%", 12_192_000, 6_096_000)]
    [InlineData("36pt", 0, 457_200)]
    public void Length_ReadsUnits(string text, long reference, long expected)
    {
        var node = double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? JsonValue.Create(number) : JsonValue.Create(text);
        var length = PresentationArguments.ReadLength(node);

        Assert.NotNull(length);
        Assert.Equal(expected, length.Value.ToEmus(reference));
    }

    /// <summary>
    /// Markdown bullets set levels, numbering and inline styles.
    /// </summary>
    [Fact]
    public void Paragraphs_ReadMarkdown()
    {
        var paragraphs = PresentationArguments.ReadParagraphs(JsonValue.Create("- First **bold** point\n  - Nested\n1. Numbered\nPlain [link](https://example.com)"));

        Assert.Equal(4, paragraphs.Count);
        Assert.Equal(0, paragraphs[0].Level);
        Assert.Contains(paragraphs[0].Runs, run => run.Text == "bold" && run.Style?.Bold == true);
        Assert.Equal(1, paragraphs[1].Level);
        Assert.Equal("number", paragraphs[2].Bullet);
        Assert.Contains(paragraphs[3].Runs, run => run.Link?.Url == "https://example.com");
    }

    /// <summary>
    /// Slide numbers are read as numbers, ranges, lists and "all", and a bad one is named.
    /// </summary>
    [Fact]
    public void Slides_ReadRangesAndLists()
    {
        Assert.Equal([2, 3, 4, 7], PresentationArguments.ReadSlides(JsonValue.Create("2-4, 7"), 10));
        Assert.Equal([1, 2, 3], PresentationArguments.ReadSlides(JsonValue.Create("all"), 3));
        Assert.Equal([5, 1], PresentationArguments.ReadSlides(new JsonArray(5, 1), 6));
        Assert.Equal([8, 9, 10], PresentationArguments.ReadSlides(JsonValue.Create("8-"), 10));

        var exception = Assert.Throws<PresentationArgumentException>(() => PresentationArguments.ReadSlides(JsonValue.Create("second"), 3));

        Assert.Contains("second", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Colours are read as hex, names and theme slots with shades, and contrast is measured.
    /// </summary>
    [Fact]
    public void Colors_ParseAndMeasureContrast()
    {
        Assert.True(PresentationColor.TryParse("#1f4e79", out var hex));
        Assert.Equal("1F4E79", hex.Hex);

        Assert.True(PresentationColor.TryParse("accent1 lighter 40%", out var theme));
        Assert.Equal("accent1", theme.ThemeSlot);
        Assert.True(theme.Brightness > 0);

        Assert.True(PresentationColor.TryParse("background1", out var alias));
        Assert.Equal("lt1", alias.ThemeSlot);

        Assert.True(PresentationColor.TryParse("none", out var none));
        Assert.True(none.IsNone);
        Assert.False(PresentationColor.TryParse("not a colour", out _));

        Assert.Equal(21, PresentationColor.ContrastRatio("000000", "FFFFFF"), 1);
        Assert.True(PresentationColor.ContrastRatio("777777", "FFFFFF") < 4.5);
    }

    /// <summary>
    /// The workspace keeps a bounded history, undoes to the exact earlier version, and forgets a removed upload.
    /// </summary>
    [Fact]
    public async Task Workspace_KeepsHistoryAndUndoes()
    {
        var root = Path.Combine(Path.GetTempPath(), "presentation-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = new FileSystemFileStore(root);
            var options = new PresentationWorkspaceOptions { MaxRevisions = 2 };
            var folder = PresentationWorkspaceStorage.GetFolder("chat-interaction", "conversation-1");
            var workspace = await PresentationWorkspace.LoadAsync(store, folder, options, TimeProvider.System);
            var deck = await workspace.AddAsync("Deck", [1], "upload-1", "deck.pptx");

            await workspace.SaveAsync(deck, [2], "second");
            await workspace.SaveAsync(deck, [3], "third");
            await workspace.SaveAsync(deck, [4], "fourth");

            Assert.Equal(2, deck.History.Count);
            // Each kept version is labelled with the change that replaced it, which is what undoing it undoes.
            Assert.Equal(["third", "fourth"], deck.History.Select(revision => revision.Description));

            var undone = await workspace.UndoAsync(deck, 1);

            Assert.Equal(["fourth"], undone);
            Assert.Equal([3], await workspace.ReadAsync(deck));

            var reloaded = await PresentationWorkspace.LoadAsync(store, folder, options, TimeProvider.System);

            Assert.Equal(deck.Id, reloaded.ActiveDeck.Id);
            Assert.True(await reloaded.RemoveImportedAsync("upload-1"));
            Assert.Empty(reloaded.State.Decks);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The checks find overflowing text, low contrast and a missing title.
    /// </summary>
    [Fact]
    public void Checker_FindsLayoutAndAccessibilityProblems()
    {
        var model = new PresentationModel { SlideWidth = PresentationUnits.WideSlideWidth, SlideHeight = PresentationUnits.WideSlideHeight };
        var paragraphs = Enumerable.Range(1, 30).Select(index => new PresentationParagraph { Runs = [new PresentationTextRun { Text = "A long line of text that fills the box " + index, Size = 24, Color = "C8C8C8" }] }).ToList();

        model.Slides.Add(new PresentationSlide
        {
            Number = 1,
            Background = PresentationFill.Solid("FFFFFF"),
            Elements =
            [
                new PresentationElement
                {
                    Id = 3,
                    Kind = PresentationElementKind.TextBox,
                    Bounds = new PresentationBounds(914_400, 914_400, 3_000_000, 900_000),
                    Text = new PresentationTextBody { Paragraphs = paragraphs },
                },
            ],
        });

        var issues = PresentationQualityChecker.Check(model, [], []);

        Assert.Contains(issues, issue => issue.Category == "layout" && issue.Message.Contains("does not fit", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Category == "accessibility" && issue.Message.Contains("contrast", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Category == "accessibility" && issue.Message.Contains("no title", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Category == "readability");
    }

    /// <summary>
    /// A hierarchy that names a parent that is not one of its items is refused with the name.
    /// </summary>
    [Fact]
    public void Composer_RefusesAnUnknownParent()
    {
        var items = new List<PresentationDiagramItem>
        {
            new() { Text = "CEO" },
            new() { Text = "CTO", Parent = "Board" },
        };

        var exception = Assert.Throws<PresentationEditException>(() => PresentationDiagramComposer.Compose("org_chart", items, new PresentationBounds(0, 0, 9_000_000, 4_000_000), [("accent1", "#FFFFFF")], null, "Org"));

        Assert.Contains("Board", exception.Message, StringComparison.Ordinal);
        Assert.Equal("hierarchy", PresentationDiagramComposer.Normalize("Org chart"));
    }
}
