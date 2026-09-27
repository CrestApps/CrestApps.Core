using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Models;
using PdfSharp.Pdf.IO;

namespace CrestApps.Core.Tests.Core.Documents.Presentations;

/// <summary>
/// Runs the presentation tools end to end: building a deck, uploading one, editing it across calls, showing
/// it, filling it from spreadsheet data, and exporting it.
/// </summary>
public sealed partial class PresentationToolTests
{
    /// <summary>
    /// A six-slide deck written in one call exports as a PowerPoint file that validates without errors and
    /// holds every slide.
    /// </summary>
    [Fact]
    public async Task CreatePresentation_WithSixSlides_ExportsAValidDeck()
    {
        using var host = new PresentationToolTestHost();

        var created = await host.InvokeAsync(PresentationToolNames.CreatePresentation, SixSlideDeck());

        Assert.StartsWith("Done.", created, StringComparison.Ordinal);
        Assert.Contains("now has 6 slides", created, StringComparison.Ordinal);

        var exported = await host.InvokeAsync(PresentationToolNames.ExportPresentation, new { });
        var marker = DocMarker().Match(exported);

        Assert.True(marker.Success, exported);

        var (document, bytes) = await host.ReadMarkerAsync(marker.Value);

        Assert.EndsWith(".pptx", document.FileName, StringComparison.Ordinal);
        Assert.Empty(await host.Engine.ValidateAsync(bytes, TestContext.Current.CancellationToken));

        var model = await host.Engine.ReadAsync(bytes, PresentationReadOptions.TextOnly, TestContext.Current.CancellationToken);

        Assert.Equal(6, model.Slides.Count);
        Assert.Equal("Quarterly Review", model.Slides[0].Title);
        Assert.Equal("Next steps", model.Slides[5].Title);
        Assert.Contains(model.Slides[2].AllElements(), element => element.Chart is not null);
        Assert.Contains(model.Slides[3].AllElements(), element => element.Table is not null);
        Assert.Contains(model.Slides[4].AllElements(), element => element.Kind == PresentationElementKind.Group);
        Assert.False(string.IsNullOrWhiteSpace(model.Slides[1].Notes));
    }

    /// <summary>
    /// An uploaded deck is imported on first use, listed, and shown as pictures with markers.
    /// </summary>
    [Fact]
    public async Task UploadedDeck_IsListedAndPreviewed()
    {
        using var host = new PresentationToolTestHost();
        var upload = await CreateDeckAsync(host, "Launch plan", ["Why now", "The plan", "Budget"]);

        await host.UploadAsync("launch.pptx", upload);

        var outline = await host.InvokeAsync(PresentationToolNames.GetPresentationOutline, new { });

        Assert.Contains("Loaded the uploaded deck", outline, StringComparison.Ordinal);
        Assert.Contains("Why now", outline, StringComparison.Ordinal);
        Assert.Contains("Budget", outline, StringComparison.Ordinal);

        var preview = await host.InvokeAsync(PresentationToolNames.PreviewPresentation, new { slides = "1-2" });
        var markers = FigMarker().Matches(preview).Select(match => match.Value).Distinct().ToList();

        Assert.Equal(2, markers.Count);

        var (document, bytes) = await host.ReadMarkerAsync(markers[1]);
        var svg = Encoding.UTF8.GetString(bytes);

        Assert.EndsWith(".svg", document.FileName, StringComparison.Ordinal);
        Assert.StartsWith("<svg", svg.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("Why now", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Changes made in one call are there in the next, in the preview and in the exported file, while the
    /// upload itself is never changed.
    /// </summary>
    [Fact]
    public async Task Edits_PersistAcrossCallsAndReachTheExport()
    {
        using var host = new PresentationToolTestHost();
        var original = await CreateDeckAsync(host, "Plan", ["Goals", "Risks"]);
        var upload = await host.UploadAsync("plan.pptx", original);

        var updated = await host.InvokeAsync(PresentationToolNames.UpdateSlide, new { slide = 2, title = "Goals for 2027", body = "- Grow revenue\n- Hire five engineers" });

        Assert.Contains("Loaded the uploaded deck", updated, StringComparison.Ordinal);
        Assert.Contains("Done.", updated, StringComparison.Ordinal);

        var added = await host.InvokeAsync(PresentationToolNames.AddSlide, new { slides = new object[] { new { title = "Timeline", position = 3 } } });

        Assert.Contains("now has 4 slides", added, StringComparison.Ordinal);

        var content = await host.InvokeAsync(PresentationToolNames.GetSlideContent, new { slide = 2 });

        Assert.Contains("Goals for 2027", content, StringComparison.Ordinal);
        Assert.Contains("Hire five engineers", content, StringComparison.Ordinal);

        var model = await host.Engine.ReadAsync(await host.ReadDeckAsync(), PresentationReadOptions.TextOnly, TestContext.Current.CancellationToken);

        Assert.Equal(["Plan", "Goals for 2027", "Timeline", "Risks"], model.Slides.Select(slide => slide.Title));
        Assert.Equal(original, await host.ReadFileAsync(upload.StoredFilePath));
    }

    /// <summary>
    /// A request that cannot be carried out changes nothing and says what was wrong.
    /// </summary>
    [Fact]
    public async Task InvalidEdit_ChangesNothing()
    {
        using var host = new PresentationToolTestHost();
        await host.InvokeAsync(PresentationToolNames.CreatePresentation, new { name = "Deck", slides = new object[] { new { title = "One" } } });
        var before = await host.ReadDeckAsync();

        var response = await host.InvokeAsync(PresentationToolNames.UpdateSlideElement, new { slide = 1, element = "#9999", rotation = 45 });

        Assert.StartsWith("Nothing was changed.", response, StringComparison.Ordinal);
        Assert.Equal(before, await host.ReadDeckAsync());
    }

    /// <summary>
    /// Undo restores the deck exactly as it was.
    /// </summary>
    [Fact]
    public async Task Undo_RestoresThePreviousVersion()
    {
        using var host = new PresentationToolTestHost();
        await host.InvokeAsync(PresentationToolNames.CreatePresentation, new { name = "Deck", slides = new object[] { new { title = "One" }, new { title = "Two" } } });
        var before = await host.ReadDeckAsync();

        await host.InvokeAsync(PresentationToolNames.DeleteSlide, new { slides = 2 });

        var undone = await host.InvokeAsync(PresentationToolNames.UndoPresentationChange, new { });

        Assert.StartsWith("Undid 1 change", undone, StringComparison.Ordinal);
        Assert.Equal(before, await host.ReadDeckAsync());
    }

    /// <summary>
    /// A chart and a table take their rows from an uploaded spreadsheet, and a linked chart follows a refresh.
    /// </summary>
    [Fact]
    public async Task ChartAndTable_ComeFromTabularData()
    {
        using var host = new PresentationToolTestHost();
        await host.UploadAsync("sales.csv", "region,revenue,cost\nNorth,120,80\nSouth,90,70\nWest,150,95\n"u8.ToArray(), "text/csv");

        var created = await host.InvokeAsync(PresentationToolNames.CreatePresentation, new
        {
            name = "Sales",
            slides = new object[]
            {
                new { layout = "title_only", title = "Revenue by region", elements = new object[] { new { type = "chart", tabular_sql = "SELECT region, revenue FROM sales ORDER BY region", chart = new { kind = "column" }, link_data = true, alt_text = "Revenue by region" } } },
                new { layout = "title_only", title = "Detail", elements = new object[] { new { type = "table", tabular_sql = "SELECT region, revenue, cost FROM sales ORDER BY region" } } },
            },
        });

        Assert.StartsWith("Done.", created, StringComparison.Ordinal);

        var model = await host.Engine.ReadAsync(await host.ReadDeckAsync(), PresentationReadOptions.TextOnly, TestContext.Current.CancellationToken);
        var chart = model.Slides[0].AllElements().Single(element => element.Chart is not null).Chart;
        var table = model.Slides[1].AllElements().Single(element => element.Table is not null).Table.ToText();

        Assert.Equal(["North", "South", "West"], chart.Categories);
        Assert.Equal([120d, 90d, 150d], chart.Series.Single().Values.Select(value => value ?? 0));
        Assert.Equal(["region", "revenue", "cost"], table[0]);
        Assert.Equal(4, table.Count);

        var workspace = await host.LoadWorkspaceAsync();

        Assert.Single(workspace.ActiveDeck.DataLinks);

        var refreshed = await host.InvokeAsync(PresentationToolNames.RefreshSlideData, new { });

        Assert.StartsWith("Done.", refreshed, StringComparison.Ordinal);
        Assert.Contains("Filled chart", refreshed, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the host cannot show pictures, the preview describes the slides in words.
    /// </summary>
    [Fact]
    public async Task Preview_WithoutPictures_FallsBackToText()
    {
        using var host = new PresentationToolTestHost(canShowFigures: false);
        await host.InvokeAsync(PresentationToolNames.CreatePresentation, new { name = "Deck", slides = new object[] { new { title = "Hello there", body = "- A point" } } });

        var preview = await host.InvokeAsync(PresentationToolNames.PreviewPresentation, new { slides = 1 });

        Assert.DoesNotMatch(FigMarker(), preview);
        Assert.Contains("cannot show pictures", preview, StringComparison.Ordinal);
        Assert.Contains("Hello there", preview, StringComparison.Ordinal);
    }

    /// <summary>
    /// The review finds a picture without alternative text and a slide without a title.
    /// </summary>
    [Fact]
    public async Task Check_ReportsAccessibilityProblems()
    {
        using var host = new PresentationToolTestHost();
        await host.UploadAsync("photo.png", PresentationTestImages.Png(40, 30), "image/png");
        await host.InvokeAsync(PresentationToolNames.CreatePresentation, new
        {
            name = "Deck",
            slides = new object[]
            {
                new { title = "Pictures", elements = new object[] { new { type = "image", image_document_id = "photo.png", alt_text = string.Empty } } },
                new { layout = "blank" },
            },
        });

        await host.InvokeAsync(PresentationToolNames.UpdateSlideElement, new { slide = 1, element = "picture", alt_text = string.Empty });

        var review = await host.InvokeAsync(PresentationToolNames.CheckPresentation, new { categories = new[] { "accessibility", "file" } });

        Assert.Contains("slide 2: The slide has no title", review, StringComparison.Ordinal);
        Assert.DoesNotContain("Schema problem", review, StringComparison.Ordinal);
    }

    /// <summary>
    /// A diagram drawn from a list is a group of editable shapes that validates.
    /// </summary>
    [Fact]
    public async Task Diagram_IsDrawnAsEditableShapes()
    {
        using var host = new PresentationToolTestHost();
        await host.InvokeAsync(PresentationToolNames.CreatePresentation, new { name = "Deck", slides = new object[] { new { title = "How it works" } } });

        var response = await host.InvokeAsync(PresentationToolNames.GenerateSlideDiagram, new
        {
            slide = 1,
            diagram_type = "process",
            items = new object[] { "Plan", new { text = "Build", detail = "Two sprints" }, "Launch" },
        });

        Assert.StartsWith("Done.", response, StringComparison.Ordinal);

        var package = await host.ReadDeckAsync();
        var model = await host.Engine.ReadAsync(package, PresentationReadOptions.TextOnly, TestContext.Current.CancellationToken);
        var group = model.Slides[0].Elements.Single(element => element.Kind == PresentationElementKind.Group);

        Assert.Equal(3, group.Children.Count(child => child.Text?.HasText == true));
        Assert.Equal(2, group.Children.Count(child => child.Kind == PresentationElementKind.Connector));
        Assert.Empty(await host.Engine.ValidateAsync(package, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Rewriting a paragraph keeps its level, and find and replace reaches every slide.
    /// </summary>
    [Fact]
    public async Task UpdateSlideText_RewritesInPlace()
    {
        using var host = new PresentationToolTestHost();
        await host.InvokeAsync(PresentationToolNames.CreatePresentation, new
        {
            name = "Deck",
            slides = new object[]
            {
                new { title = "Acme overview", body = "- First\n  - Nested detail\n- Second" },
                new { title = "Acme next", body = "- Acme grows" },
            },
        });

        var outline = await host.InvokeAsync(PresentationToolNames.ExtractPresentationContent, new { kind = "text", slides = 1 });
        var body = BodyId().Match(outline);

        Assert.True(body.Success, outline);

        var response = await host.InvokeAsync(PresentationToolNames.UpdateSlideText, new
        {
            paragraphs = new object[] { new { slide = 1, element = body.Groups["id"].Value, paragraph = 2, text = "Rewritten detail" } },
            replace = new object[] { new { find = "Acme", replace_with = "Contoso" } },
        });

        Assert.StartsWith("Done.", response, StringComparison.Ordinal);

        var model = await host.Engine.ReadAsync(await host.ReadDeckAsync(), PresentationReadOptions.TextOnly, TestContext.Current.CancellationToken);
        var paragraphs = model.Slides[0].AllElements().First(element => !element.IsTitle && element.Text?.HasText == true).Text.Paragraphs;

        Assert.Equal("Rewritten detail", paragraphs[1].Text);
        Assert.Equal(1, paragraphs[1].Level);
        Assert.Equal("Contoso overview", model.Slides[0].Title);
        Assert.Equal("Contoso next", model.Slides[1].Title);
    }

    /// <summary>
    /// A new deck can start from an uploaded template and keeps its design.
    /// </summary>
    [Fact]
    public async Task CreatePresentation_FromTemplate_UsesItsTheme()
    {
        using var host = new PresentationToolTestHost();
        var template = await host.Engine.CreateAsync(new PresentationCreateOptions { ThemeName = "Brand", HeadingFont = "Georgia", BodyFont = "Verdana" }, TestContext.Current.CancellationToken);
        await host.UploadAsync("brand.potx", template);

        var created = await host.InvokeAsync(PresentationToolNames.CreatePresentation, new { name = "Branded", template_document_id = "brand.potx", slides = new object[] { new { title = "Hello" } } });

        Assert.Contains("Done.", created, StringComparison.Ordinal);

        var model = await host.Engine.ReadAsync(await host.ReadDeckAsync("Branded"), PresentationReadOptions.TextOnly, TestContext.Current.CancellationToken);

        Assert.Equal("Georgia", model.Theme.HeadingFont);
        Assert.Equal("Verdana", model.Theme.BodyFont);
        Assert.Equal("Hello", Assert.Single(model.Slides).Title);
    }

    /// <summary>
    /// The outline and notes export as text the model can show, or as a document.
    /// </summary>
    [Fact]
    public async Task ExportContent_WritesOutlineAndScript()
    {
        using var host = new PresentationToolTestHost();
        await host.InvokeAsync(PresentationToolNames.CreatePresentation, SixSlideDeck());

        var outline = await host.InvokeAsync(PresentationToolNames.ExportPresentationContent, new { kind = "outline" });

        Assert.Contains("## 2. Agenda", outline, StringComparison.Ordinal);

        var script = await host.InvokeAsync(PresentationToolNames.ExportPresentationContent, new { kind = "script" });

        Assert.Contains("Total: about", script, StringComparison.Ordinal);

        var file = await host.InvokeAsync(PresentationToolNames.ExportPresentationContent, new { kind = "notes", format = "md" });

        Assert.Matches(DocMarker(), file);
    }

    /// <summary>
    /// Where the PDF package is installed, a deck exports as a PDF with one page per shown slide.
    /// </summary>
    [Fact]
    public async Task ExportPresentation_AsPdf_HasAPagePerSlide()
    {
        using var host = new PresentationToolTestHost(configure: services => services.AddCoreAIPresentationPdfExport());
        await host.InvokeAsync(PresentationToolNames.CreatePresentation, SixSlideDeck());
        await host.InvokeAsync(PresentationToolNames.UpdateSlide, new { slide = 4, hidden = true });

        var exported = await host.InvokeAsync(PresentationToolNames.ExportPresentation, new { format = "pdf" });
        var marker = DocMarker().Match(exported);

        Assert.True(marker.Success, exported);

        var (document, bytes) = await host.ReadMarkerAsync(marker.Value);

        Assert.EndsWith(".pdf", document.FileName, StringComparison.Ordinal);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));

        using var pdf = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

        Assert.Equal(5, pdf.PageCount);
    }

    /// <summary>
    /// Without the PDF package, a PDF export is refused in words the model can act on.
    /// </summary>
    [Fact]
    public async Task ExportPresentation_AsPdf_WithoutRenderer_SaysSo()
    {
        using var host = new PresentationToolTestHost();
        await host.InvokeAsync(PresentationToolNames.CreatePresentation, new { name = "Deck", slides = new object[] { new { title = "One" } } });

        var exported = await host.InvokeAsync(PresentationToolNames.ExportPresentation, new { format = "pdf" });

        Assert.StartsWith("Nothing was changed. PDF export is not available", exported, StringComparison.Ordinal);
    }

    private static object SixSlideDeck()
    {
        return new
        {
            name = "Quarterly Review",
            theme = "corporate",
            slides = new object[]
            {
                new { layout = "title", title = "Quarterly Review", subtitle = "Q3 results" },
                new { title = "Agenda", body = new[] { "Results", "Customers", "Next steps" }, notes = "Walk through the three parts." },
                new
                {
                    layout = "title_only",
                    title = "Revenue",
                    elements = new object[]
                    {
                        new { type = "chart", chart = new { kind = "column", categories = new[] { "Jul", "Aug", "Sep" }, series = new object[] { new { name = "Revenue", values = new[] { 10, 12, 15 } } } }, alt_text = "Revenue grew each month" },
                    },
                },
                new
                {
                    layout = "title_only",
                    title = "Customers",
                    elements = new object[] { new { type = "table", rows = new[] { new[] { "Segment", "Count" }, new[] { "Enterprise", "12" }, new[] { "SMB", "40" } } } },
                },
                new
                {
                    layout = "title_only",
                    title = "How we work",
                    elements = new object[] { new { type = "diagram", diagram_type = "cycle", items = new[] { "Listen", "Build", "Measure" } } },
                },
                new { title = "Next steps", body = "- Hire\n- Launch", notes = "Close with the ask." },
            },
        };
    }

    private static async Task<byte[]> CreateDeckAsync(PresentationToolTestHost host, string title, string[] slideTitles)
    {
        var package = await host.Engine.CreateAsync(new PresentationCreateOptions { Title = title }, TestContext.Current.CancellationToken);
        var edits = new List<AI.Documents.Presentations.Editing.PresentationEdit>
        {
            new AI.Documents.Presentations.Editing.AddSlideEdit { Layout = "title", Title = title },
        };

        edits.AddRange(slideTitles.Select(slideTitle => new AI.Documents.Presentations.Editing.AddSlideEdit
        {
            Title = slideTitle,
            Body = [AI.Documents.Presentations.Editing.PresentationParagraphSpec.FromText("A point about " + slideTitle)],
        }));

        var result = await host.Engine.EditAsync(package, edits, new PresentationEditContext(), TestContext.Current.CancellationToken);

        return result.Package;
    }

    [GeneratedRegex(@"\[doc:\d+\]")]
    private static partial Regex DocMarker();

    [GeneratedRegex(@"\[fig:\d+\]")]
    private static partial Regex FigMarker();

    [GeneratedRegex(@"#(?<id>\d+) body ¶2")]
    private static partial Regex BodyId();
}
