using CrestApps.Core.AI.Documents.OpenXml.Presentations;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Presentations.Models;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Documents.Presentations;

/// <summary>
/// Verifies the Open XML presentation engine: decks it creates and edits open, validate and read back as
/// written, and a batch of edits is applied whole or not at all.
/// </summary>
public sealed class OpenXmlPresentationEngineTests
{
    private readonly OpenXmlPresentationEngine _engine = new(TimeProvider.System, NullLogger<OpenXmlPresentationEngine>.Instance);

    /// <summary>
    /// Every theme preset makes a deck that validates and carries the preset's fonts and colours.
    /// </summary>
    /// <param name="preset">The preset's name.</param>
    [Theory]
    [InlineData("office")]
    [InlineData("modern")]
    [InlineData("corporate")]
    [InlineData("dark")]
    [InlineData("vibrant")]
    [InlineData("minimal")]
    [InlineData("nature")]
    [InlineData("warm")]
    [InlineData("ocean")]
    public async Task Create_EveryPreset_Validates(string preset)
    {
        Assert.True(PresentationThemePresets.TryGet(preset, out var theme));

        var options = new PresentationCreateOptions
        {
            ThemeName = theme.Name,
            HeadingFont = theme.HeadingFont,
            BodyFont = theme.BodyFont,
            DarkBackground = theme.DarkBackground,
        };

        foreach (var (slot, color) in theme.Colors)
        {
            options.ThemeColors[slot] = "#" + color;
        }

        var package = await _engine.CreateAsync(options, TestContext.Current.CancellationToken);
        var result = await EditAsync(package, new AddSlideEdit { Layout = "title", Title = "Hello" }, new AddSlideEdit { Title = "Points", Body = [PresentationParagraphSpec.FromText("One")] });
        var model = await ReadAsync(result.Package);

        Assert.Empty(await _engine.ValidateAsync(result.Package, TestContext.Current.CancellationToken));
        Assert.Equal(theme.HeadingFont, model.Theme.HeadingFont);
        Assert.Equal(theme.BodyFont, model.Theme.BodyFont);
        Assert.Equal(theme.Colors["accent1"], model.Theme.Colors["accent1"], ignoreCase: true);
    }

    /// <summary>
    /// A slide can be made from every layout kind, and each validates.
    /// </summary>
    /// <param name="layout">The layout kind.</param>
    /// <param name="expectedType">The layout type the slide is built on.</param>
    [Theory]
    [InlineData("title", "title")]
    [InlineData("title_and_content", "obj")]
    [InlineData("section_header", "secHead")]
    [InlineData("two_content", "twoObj")]
    [InlineData("comparison", "twoTxTwoObj")]
    [InlineData("title_only", "titleOnly")]
    [InlineData("blank", "blank")]
    [InlineData("content_with_caption", "objTx")]
    [InlineData("picture_with_caption", "picTx")]
    public async Task AddSlide_EveryLayout_Validates(string layout, string expectedType)
    {
        var package = await _engine.CreateAsync(new PresentationCreateOptions(), TestContext.Current.CancellationToken);
        var result = await EditAsync(package, new AddSlideEdit
        {
            Layout = layout,
            Title = "Title",
            Subtitle = "Subtitle",
            Body = [PresentationParagraphSpec.FromText("Left point")],
            SecondBody = [PresentationParagraphSpec.FromText("Right point")],
            FirstHeading = "Before",
            SecondHeading = "After",
            Notes = "Say this.",
        });

        var slide = Assert.Single((await ReadAsync(result.Package)).Slides);

        Assert.Empty(await _engine.ValidateAsync(result.Package, TestContext.Current.CancellationToken));
        Assert.Equal(expectedType, slide.LayoutType);
        Assert.Equal("Say this.", slide.Notes);
    }

    /// <summary>
    /// When one edit in a batch fails, the batch throws and the deck the caller holds is untouched.
    /// </summary>
    [Fact]
    public async Task Edit_WhenOneEditFails_AppliesNothing()
    {
        var package = await DeckAsync("One", "Two");
        var copy = package.ToArray();

        var exception = await Assert.ThrowsAsync<PresentationEditException>(() => _engine.EditAsync(
            package,
            [
                new UpdateSlideEdit { Slide = 1, Title = "Changed" },
                new DeleteElementsEdit { Slide = 2, Elements = ["#999"] },
            ],
            new PresentationEditContext(),
            TestContext.Current.CancellationToken));

        Assert.Contains("#999", exception.Message, StringComparison.Ordinal);
        Assert.Equal(copy, package);
        Assert.Equal("One", (await ReadAsync(package)).Slides[0].Title);
    }

    /// <summary>
    /// A duplicated slide carries its chart as a separate, editable copy.
    /// </summary>
    [Fact]
    public async Task DuplicateSlide_CopiesChartsIndependently()
    {
        var package = await DeckAsync("Chart");
        var withChart = await EditAsync(package, new InsertElementsEdit { Slide = 1, Elements = [Chart([1, 2, 3])] });
        var duplicated = await EditAsync(withChart.Package, new DuplicateSlideEdit { Slide = 1 });
        var chartId = (await ReadAsync(duplicated.Package)).Slides[1].AllElements().Single(element => element.Chart is not null).Id;
        var changed = await EditAsync(duplicated.Package, new UpdateChartEdit
        {
            Slide = 2,
            Element = chartId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Chart = new PresentationChartSpec { Series = [new PresentationChartSeriesSpec { Name = "Sales", Values = [7, 8, 9] }] },
        });

        var model = await ReadAsync(changed.Package);

        Assert.Empty(await _engine.ValidateAsync(changed.Package, TestContext.Current.CancellationToken));
        Assert.Equal([1d, 2d, 3d], model.Slides[0].AllElements().Single(element => element.Chart is not null).Chart.Series[0].Values.Select(value => value ?? 0));
        Assert.Equal([7d, 8d, 9d], model.Slides[1].AllElements().Single(element => element.Chart is not null).Chart.Series[0].Values.Select(value => value ?? 0));

        using var stream = new MemoryStream(changed.Package);
        using var document = PresentationDocument.Open(stream, false);

        Assert.Equal(2, document.PresentationPart.SlideParts.SelectMany(slide => slide.ChartParts).Distinct().Count());
    }

    /// <summary>
    /// Moving and deleting slides keeps the order the caller asked for.
    /// </summary>
    [Fact]
    public async Task MoveAndDelete_KeepTheRequestedOrder()
    {
        var package = await DeckAsync("A", "B", "C", "D");
        var result = await EditAsync(package, new MoveSlideEdit { Slide = 4, Position = 1 }, new DeleteSlidesEdit { Slides = [3] });

        Assert.Equal(["D", "A", "C"], (await ReadAsync(result.Package)).Slides.Select(slide => slide.Title));
        Assert.Equal(3, result.SlideCount);
    }

    /// <summary>
    /// Find and replace finds text split across differently formatted runs.
    /// </summary>
    [Fact]
    public async Task ReplaceText_MatchesAcrossRuns()
    {
        var package = await DeckAsync("Start");
        var result = await EditAsync(
            package,
            new UpdateSlideEdit
            {
                Slide = 1,
                Body =
                [
                    new PresentationParagraphSpec
                    {
                        Runs =
                        [
                            new PresentationRunSpec { Text = "Con" },
                            new PresentationRunSpec { Text = "toso", Style = new PresentationTextStyle { Bold = true } },
                            new PresentationRunSpec { Text = " Ltd" },
                        ],
                    },
                ],
            },
            new ReplaceTextEdit { Find = "Contoso", Replace = "Fabrikam" });

        var body = (await ReadAsync(result.Package)).Slides[0].AllElements().First(element => !element.IsTitle && element.Text?.HasText == true);

        Assert.Equal("Fabrikam Ltd", body.Text.PlainText);
    }

    /// <summary>
    /// Text too long for its box is shrunk to fit, and the edit says so.
    /// </summary>
    [Fact]
    public async Task LongText_IsShrunkAndReported()
    {
        var package = await DeckAsync("Dense");
        var body = Enumerable.Range(1, 18).Select(index => PresentationParagraphSpec.FromText($"Point number {index} explains a detail at some length so that the box overflows")).ToList();
        var result = await EditAsync(package, new UpdateSlideEdit { Slide = 1, Body = body });
        var element = (await ReadAsync(result.Package)).Slides[0].AllElements().First(candidate => !candidate.IsTitle && candidate.Text?.HasText == true);

        Assert.True(element.Text.FontScale < 1);
        Assert.Contains(result.Warnings, warning => warning.Contains("only fits at", StringComparison.Ordinal) || warning.Contains("does not fit", StringComparison.Ordinal));
    }

    /// <summary>
    /// Footers put slide numbers on content slides and leave the title slide alone.
    /// </summary>
    [Fact]
    public async Task SetFooter_SkipsTheTitleSlide()
    {
        var package = await _engine.CreateAsync(new PresentationCreateOptions(), TestContext.Current.CancellationToken);
        var deck = await EditAsync(package, new AddSlideEdit { Layout = "title", Title = "Cover" }, new AddSlideEdit { Title = "Content", Body = [PresentationParagraphSpec.FromText("x")] });
        var result = await EditAsync(deck.Package, new SetFooterEdit { FooterText = "Confidential", SlideNumbers = true });
        var model = await ReadAsync(result.Package);

        Assert.Empty(await _engine.ValidateAsync(result.Package, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(model.Slides[0].Elements, element => element.PlaceholderType is "ftr" or "sldNum");
        Assert.Contains(model.Slides[1].Elements, element => element.PlaceholderType == "ftr" && element.Text.PlainText == "Confidential");
        Assert.Contains(model.Slides[1].Elements, element => element.PlaceholderType == "sldNum");
    }

    /// <summary>
    /// Sections are written in order and each slide reports its section.
    /// </summary>
    [Fact]
    public async Task UpdateSections_GroupsSlides()
    {
        var package = await DeckAsync("A", "B", "C");
        var result = await EditAsync(package, new UpdateSectionsEdit
        {
            Sections =
            [
                new PresentationSectionSpec { Name = "Intro", FirstSlide = 1 },
                new PresentationSectionSpec { Name = "Detail", FirstSlide = 2 },
            ],
        });

        var model = await ReadAsync(result.Package);

        Assert.Empty(await _engine.ValidateAsync(result.Package, TestContext.Current.CancellationToken));
        Assert.Equal(["Intro", "Detail", "Detail"], model.Slides.Select(slide => slide.SectionName));
    }

    /// <summary>
    /// A table grows by rows and columns in place and keeps its header.
    /// </summary>
    [Fact]
    public async Task UpdateTable_InsertsRowsAndColumns()
    {
        var package = await DeckAsync("Table");
        var inserted = await EditAsync(package, new InsertElementsEdit
        {
            Slide = 1,
            Elements = [new PresentationElementSpec { Kind = PresentationElementSpecKind.Table, Table = new PresentationTableSpec { Rows = [["Name", "Value"], ["A", "1"]] } }],
        });

        var tableId = (await ReadAsync(inserted.Package)).Slides[0].AllElements().Single(element => element.Table is not null).Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var result = await EditAsync(inserted.Package, new UpdateTableEdit
        {
            Slide = 1,
            Element = tableId,
            InsertRows = [["B", "2"]],
            InsertColumns = [["Note", "first", "second"]],
            Cells = [new PresentationTableCellEdit { Row = 2, Column = 2, Text = "10" }],
        });

        var rows = (await ReadAsync(result.Package)).Slides[0].AllElements().Single(element => element.Table is not null).Table.ToText();

        Assert.Empty(await _engine.ValidateAsync(result.Package, TestContext.Current.CancellationToken));
        Assert.Equal(["Name", "Value", "Note"], rows[0]);
        Assert.Equal(["A", "10", "first"], rows[1]);
        Assert.Equal(["B", "2", "second"], rows[2]);
    }

    /// <summary>
    /// A chart can change type and keep its data, and the result validates.
    /// </summary>
    /// <param name="kind">The new chart type.</param>
    [Theory]
    [InlineData("bar")]
    [InlineData("line")]
    [InlineData("pie")]
    [InlineData("doughnut")]
    [InlineData("stacked_column")]
    [InlineData("area")]
    [InlineData("radar")]
    public async Task UpdateChart_ChangesType(string kind)
    {
        var package = await DeckAsync("Chart");
        var inserted = await EditAsync(package, new InsertElementsEdit { Slide = 1, Elements = [Chart([3, 5, 4])] });
        var chartId = (await ReadAsync(inserted.Package)).Slides[0].AllElements().Single(element => element.Chart is not null).Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var result = await EditAsync(inserted.Package, new UpdateChartEdit { Slide = 1, Element = chartId, Chart = new PresentationChartSpec { Kind = kind, Title = "Changed" } });
        var chart = (await ReadAsync(result.Package)).Slides[0].AllElements().Single(element => element.Chart is not null).Chart;

        Assert.Empty(await _engine.ValidateAsync(result.Package, TestContext.Current.CancellationToken));
        Assert.Equal("Changed", chart.Title);
        Assert.Equal([3d, 5d, 4d], chart.Series[0].Values.Select(value => value ?? 0));
    }

    /// <summary>
    /// A template is imported as an ordinary deck with its design.
    /// </summary>
    [Fact]
    public async Task Import_Template_BecomesAPresentation()
    {
        var package = await _engine.CreateAsync(new PresentationCreateOptions { HeadingFont = "Georgia" }, TestContext.Current.CancellationToken);

        using (var stream = new MemoryStream())
        {
            stream.Write(package);
            stream.Position = 0;

            using var document = PresentationDocument.Open(stream, true);
            document.ChangeDocumentType(DocumentFormat.OpenXml.PresentationDocumentType.Template);
            document.Save();
            package = stream.ToArray();
        }

        await using var source = new MemoryStream(package);
        var imported = await _engine.ImportAsync(source, "brand.potx", TestContext.Current.CancellationToken);

        using var check = new MemoryStream(imported);
        using var opened = PresentationDocument.Open(check, false);

        Assert.Equal(DocumentFormat.OpenXml.PresentationDocumentType.Presentation, opened.DocumentType);
        Assert.Equal("Georgia", (await ReadAsync(imported)).Theme.HeadingFont);
    }

    /// <summary>
    /// A diagram element is laid out inside its area as a group of shapes and connectors.
    /// </summary>
    /// <param name="kind">The diagram kind.</param>
    [Theory]
    [InlineData("process")]
    [InlineData("chevron")]
    [InlineData("cycle")]
    [InlineData("timeline")]
    [InlineData("hierarchy")]
    [InlineData("pyramid")]
    [InlineData("funnel")]
    [InlineData("matrix")]
    [InlineData("venn")]
    [InlineData("cards")]
    public async Task Diagram_EveryKind_StaysOnTheSlideAndValidates(string kind)
    {
        var package = await DeckAsync("Diagram");
        var items = kind switch
        {
            "venn" => new[] { "A", "B", "C" },
            "matrix" => new[] { "A", "B", "C", "D" },
            _ => new[] { "A", "B", "C", "D" },
        };

        var result = await EditAsync(package, new InsertElementsEdit
        {
            Slide = 1,
            Elements =
            [
                new PresentationElementSpec
                {
                    Kind = PresentationElementSpecKind.Diagram,
                    Diagram = new PresentationDiagramSpec { Kind = kind, Items = items.Select(text => new PresentationDiagramItem { Text = text }).ToList() },
                },
            ],
        });

        var model = await ReadAsync(result.Package);
        var group = model.Slides[0].Elements.Single(element => element.Kind == PresentationElementKind.Group);
        var slide = new PresentationBounds(0, 0, model.SlideWidth, model.SlideHeight);

        Assert.Empty(await _engine.ValidateAsync(result.Package, TestContext.Current.CancellationToken));
        Assert.All(group.Children, child => Assert.True(slide.Intersect(child.Bounds).Area() >= child.Bounds.Area() * 0.99, $"{child.Name} leaves the slide."));
        Assert.Equal(items.Length, group.Children.Count(child => child.Text?.HasText == true && items.Contains(child.Text.Paragraphs[0].Text)));
    }

    private static PresentationElementSpec Chart(double[] values)
    {
        return new PresentationElementSpec
        {
            Kind = PresentationElementSpecKind.Chart,
            Chart = new PresentationChartSpec
            {
                Kind = "column",
                Categories = ["X", "Y", "Z"],
                Series = [new PresentationChartSeriesSpec { Name = "Sales", Values = values.Select(value => (double?)value).ToList() }],
            },
        };
    }

    private async Task<byte[]> DeckAsync(params string[] titles)
    {
        var package = await _engine.CreateAsync(new PresentationCreateOptions(), TestContext.Current.CancellationToken);
        var edits = titles.Select(title => (PresentationEdit)new AddSlideEdit { Layout = "title_and_content", Title = title }).ToArray();

        return (await EditAsync(package, edits)).Package;
    }

    private Task<PresentationEditResult> EditAsync(byte[] package, params PresentationEdit[] edits)
    {
        return _engine.EditAsync(package, edits, new PresentationEditContext(), TestContext.Current.CancellationToken);
    }

    private Task<PresentationModel> ReadAsync(byte[] package)
    {
        return _engine.ReadAsync(package, PresentationReadOptions.TextOnly, TestContext.Current.CancellationToken);
    }
}
