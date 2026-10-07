using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word;
using CrestApps.Core.AI.Documents.Word.Charts;
using CrestApps.Core.AI.Documents.Word.Fields;
using CrestApps.Core.AI.Documents.Word.Rendering;
using CrestApps.Core.AI.Documents.Word.Tools;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Documents.Word;

public sealed partial class WordRenderingTests
{
    private const string AlternateContentParagraph = """
        <w:p xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:v="urn:schemas-microsoft-com:vml">
          <w:r>
            <mc:AlternateContent>
              <mc:Choice Requires="wps">
                <w:drawing>
                  <wp:inline distT="0" distB="0" distL="0" distR="0">
                    <wp:extent cx="1828800" cy="457200"/>
                    <wp:docPr id="1" name="Text Box 1"/>
                    <a:graphic>
                      <a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape">
                        <wps:wsp>
                          <wps:cNvSpPr txBox="1"/>
                          <wps:spPr>
                            <a:xfrm><a:off x="0" y="0"/><a:ext cx="1828800" cy="457200"/></a:xfrm>
                            <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                          </wps:spPr>
                          <wps:txbx>
                            <w:txbxContent><w:p><w:r><w:t>Inside box</w:t></w:r></w:p></w:txbxContent>
                          </wps:txbx>
                          <wps:bodyPr/>
                        </wps:wsp>
                      </a:graphicData>
                    </a:graphic>
                  </wp:inline>
                </w:drawing>
              </mc:Choice>
              <mc:Fallback>
                <w:pict><v:rect style="width:144pt;height:36pt"><v:textbox><w:txbxContent><w:p><w:r><w:t>Inside box</w:t></w:r></w:p></w:txbxContent></v:textbox></v:rect></w:pict>
              </mc:Fallback>
            </mc:AlternateContent>
          </w:r>
        </w:p>
        """;

    private const string PictureParagraph = """
        <w:p xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <w:r>
            <w:drawing>
              <wp:inline distT="0" distB="0" distL="0" distR="0">
                <wp:extent cx="914400" cy="914400"/>
                <wp:docPr id="2" name="Picture 2"/>
                <a:graphic>
                  <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                    <pic:pic>
                      <pic:nvPicPr><pic:cNvPr id="2" name="photo.png"/><pic:cNvPicPr/></pic:nvPicPr>
                      <pic:blipFill><a:blip r:embed="RID"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
                      <pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
                    </pic:pic>
                  </a:graphicData>
                </a:graphic>
              </wp:inline>
            </w:drawing>
          </w:r>
        </w:p>
        """;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Parse_HugeRange_ReturnsDocumentPagesQuickly()
    {
        var pages = await RunAsync(() => WordPageRange.Parse("1-2147483647", 3));

        Assert.Equal([1, 2, 3], pages);
    }

    [Fact]
    public async Task Parse_RangePastDocument_ReturnsNoPages()
    {
        var pages = await RunAsync(() => WordPageRange.Parse("2000000000-2147483647, 0", 4));

        Assert.Empty(pages);
    }

    [Fact]
    public async Task Layout_DeeplyNestedTables_StopsDescendingWithoutCrashing()
    {
        using var package = WordPackage.Create(new WordDesign());

        OpenXmlElement inner = new Paragraph(new Run(new Text("Deepest")));

        for (var depth = 0; depth < 300; depth++)
        {
            inner = new Table(new TableRow(new TableCell(inner, new Paragraph())));
        }

        Add(package, inner);

        var layout = await RunAsync(() => WordLayoutEngine.Layout(package));

        Assert.Equal(1, layout.PageCount);
        Assert.Contains(layout.Issues, issue => issue.Kind == "clipped");
        Assert.DoesNotContain("Deepest", WordPreview.PageText(layout.Pages[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Layout_HugeGridSpan_ClampsColumns()
    {
        using var package = WordPackage.Create(new WordDesign());

        Add(package, new Table(new TableRow(
            new TableCell(new TableCellProperties(new GridSpan { Val = 1_000_000_000 }), new Paragraph(new Run(new Text("Wide")))),
            new TableCell(new TableCellProperties(new GridSpan { Val = 1_000_000_000 }), new Paragraph(new Run(new Text("Wider")))))));

        var layout = await RunAsync(() => WordLayoutEngine.Layout(package));

        Assert.Equal(1, layout.PageCount);
        Assert.Contains("Wide", WordPreview.PageText(layout.Pages[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Draw_ChartWithHugeValues_Terminates()
    {
        var spec = new WordChartSpec
        {
            Type = "column",
            Labels = ["A", "B", "C", "D"],
            Series = [new WordChartSeries { Name = "Values", Values = [double.MaxValue, -double.MaxValue, double.NaN, double.PositiveInfinity] }],
            DataLabels = true,
        };

        var items = await RunAsync(() => WordChartDrawing.Draw(spec, 400, 300, null));

        Assert.InRange(items.Count, 1, 1000);
        Assert.All(items.OfType<WordTextItem>(), item => Assert.True(double.IsFinite(item.X) && double.IsFinite(item.Baseline)));
        Assert.All(items.OfType<WordRectItem>(), item => Assert.True(double.IsFinite(item.Y) && double.IsFinite(item.Height)));
    }

    [Fact]
    public async Task Draw_ChartWithVeryLongLabel_ClipsIt()
    {
        var spec = new WordChartSpec
        {
            Type = "bar",
            Labels = [new string('x', 1_000_000)],
            Series = [new WordChartSeries { Name = new string('y', 1_000_000), Values = [1] }],
            Legend = "right",
        };

        var items = await RunAsync(() => WordChartDrawing.Draw(spec, 400, 300, null));

        Assert.All(items.OfType<WordTextItem>(), item => Assert.True(item.Text.Length <= 256, $"A label of {item.Text.Length} characters was drawn."));
    }

    [Fact]
    public async Task Layout_DuplicateListLevels_NumbersFromTheFirstLevel()
    {
        using var package = WordPackage.Create(new WordDesign());
        var numberingPart = package.MainPart.AddNewPart<NumberingDefinitionsPart>();

        numberingPart.Numbering = new Numbering(
            new AbstractNum(
                new Level(new StartNumberingValue { Val = 1 }, new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1." }) { LevelIndex = 0 },
                new Level(new StartNumberingValue { Val = 1 }, new NumberingFormat { Val = NumberFormatValues.UpperRoman }, new LevelText { Val = "%1)" }) { LevelIndex = 0 },
                new Level(new StartNumberingValue { Val = 2_000_000_000 }, new NumberingFormat { Val = NumberFormatValues.LowerLetter }, new LevelText { Val = "%2." }) { LevelIndex = 1 })
            { AbstractNumberId = 1 },
            new NumberingInstance(
                new AbstractNumId { Val = 1 },
                new LevelOverride(new StartOverrideNumberingValue { Val = 5 }) { LevelIndex = 0 },
                new LevelOverride(new StartOverrideNumberingValue { Val = 9 }) { LevelIndex = 0 })
            { NumberID = 1 });

        Add(package, ListItem("First item", 0), ListItem("Nested item", 1));

        var layout = await RunAsync(() => WordLayoutEngine.Layout(package));
        var text = WordPreview.PageText(layout.Pages[0]);

        Assert.Contains("5.", text, StringComparison.Ordinal);
        Assert.Contains("Nested item", text, StringComparison.Ordinal);
        Assert.True(text.Length < 10_000, "A list started at a huge number built a huge marker.");
    }

    [Fact]
    public async Task Layout_ZeroPageSize_DrawsAFinitePage()
    {
        using var package = WordPackage.Create(new WordDesign(), new SectionProperties(
            new PageSize { Width = 0, Height = 0 },
            new Columns { ColumnCount = 32767, Space = "NaN" }));

        Add(package, new Paragraph(new Run(new Text("Sized"))));

        var layout = await RunAsync(() => WordLayoutEngine.Layout(package));
        var page = layout.Pages[0];
        var svg = WordSvgWriter.Write(page, 816);

        Assert.True(page.Width > 0 && double.IsFinite(page.Width));
        Assert.True(page.Height > 0 && double.IsFinite(page.Height));
        Assert.DoesNotContain("Infinity", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.NotNull(XElement.Parse(svg));
    }

    [Fact]
    public async Task Layout_PageLimitReachedAtColumnBreak_StopsCleanly()
    {
        using var package = WordPackage.Create(new WordDesign(), new SectionProperties(
            new PageSize { Width = 12240, Height = 15840 },
            new Columns { ColumnCount = 2, Space = "720" }));

        Add(package, new Paragraph(
            new Run(new Text("Column one")),
            new Run(new Break { Type = BreakValues.Column }),
            new Run(new Text("Column two")),
            new Run(new Break { Type = BreakValues.Column }),
            new Run(new Text("Past the limit"))));
        Add(package, new Paragraph(new Run(new Text("Never laid out"))));

        var layout = await RunAsync(() => WordLayoutEngine.Layout(package, new WordLayoutOptions { MaxPages = 1 }));

        Assert.True(layout.Truncated);
        Assert.Equal(1, layout.PageCount);
        Assert.Contains("Column two", WordPreview.PageText(layout.Pages[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Layout_PageBreakParagraph_StartsNextContentAtTopOfPage()
    {
        using var package = WordPackage.Create(new WordDesign());
        var second = Spaced(new Paragraph(new Run(new Text("Second page"))), before: 0);

        Add(package, new Paragraph(new Run(new Text("First page"))));
        Add(package, new Paragraph(new Run(new Break { Type = BreakValues.Page })));
        Add(package, second);

        var layout = await RunAsync(() => WordLayoutEngine.Layout(package));

        Assert.Equal(2, layout.PageCount);
        Assert.Equal(2, layout.PageOf(second));

        var page = layout.Pages[1];
        var text = page.Items.OfType<WordTextItem>().Single(item => item.Text.Contains("Second", StringComparison.Ordinal));

        // The paragraph's first line starts at the top of the text area: no empty line is carried over.
        Assert.InRange(text.Baseline - page.ContentTop, 0, 16);
    }

    [Fact]
    public async Task Layout_ParagraphAfterHardPageBreak_KeepsSpaceBefore()
    {
        using var package = WordPackage.Create(new WordDesign());
        var second = Spaced(new Paragraph(new Run(new Text("Spaced"))), before: 480);

        Add(package, new Paragraph(new Run(new Text("First page"), new Break { Type = BreakValues.Page })));
        Add(package, second);

        var layout = await RunAsync(() => WordLayoutEngine.Layout(package));
        var page = layout.Pages[layout.PageOf(second) - 1];
        var text = page.Items.OfType<WordTextItem>().Single(item => item.Text.Contains("Spaced", StringComparison.Ordinal));

        Assert.Equal(2, page.Index);
        Assert.InRange(text.Baseline - page.ContentTop, 24, 24 + 16);
    }

    [Fact]
    public async Task Layout_KeepWithNextBeforeTallLine_MovesParagraphToNextPage()
    {
        using var package = WordPackage.Create(new WordDesign());
        var heading = Spaced(new Paragraph(new Run(new Text("Heading"))), before: 0);
        var tall = Spaced(new Paragraph(new Run(new Text("Tall"))), before: 0, exactLine: 2400);

        heading.ParagraphProperties.KeepNext = new KeepNext();

        // A 540-point line leaves room for the heading but not for the 120-point line it is kept with.
        Add(package, Spaced(new Paragraph(new Run(new Text("Filler"))), before: 0, exactLine: 10800));
        Add(package, heading);
        Add(package, tall);

        var layout = await RunAsync(() => WordLayoutEngine.Layout(package));

        Assert.Equal(2, layout.PageOf(heading));
        Assert.Equal(2, layout.PageOf(tall));
    }

    [Fact]
    public async Task Layout_ShapeInAlternateContent_DrawsTheChoiceOnce()
    {
        using var package = WordPackage.Create(new WordDesign());

        Add(package, new Paragraph(AlternateContentParagraph));

        var layout = await RunAsync(() => WordLayoutEngine.Layout(package));
        var page = layout.Pages[0];

        Assert.Single(page.Items.OfType<WordTextItem>(), item => item.Text.Contains("Inside box", StringComparison.Ordinal));
        Assert.DoesNotContain(page.Items.OfType<WordImageItem>(), item => item.Label?.Contains("Legacy", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Layout_PictureLargerThanBudget_DrawsPlaceholder()
    {
        using var package = WordPackage.Create(new WordDesign());
        var imagePart = package.MainPart.AddImagePart(ImagePartType.Png);

        using (var stream = new MemoryStream(new byte[4096]))
        {
            imagePart.FeedData(stream);
        }

        Add(package, new Paragraph(PictureParagraph.Replace("RID", package.MainPart.GetIdOfPart(imagePart), StringComparison.Ordinal)));

        var layout = await RunAsync(() => WordLayoutEngine.Layout(package, new WordLayoutOptions { MaxPictureBytesPerPage = 1024 }));
        var image = Assert.Single(layout.Pages[0].Items.OfType<WordImageItem>());

        Assert.Null(image.Bytes);
        Assert.Contains("not drawn", image.Label, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAll_HeadingPastLayoutLimit_LeavesPageNumberBlank()
    {
        using var package = WordPackage.Create(new WordDesign());
        var reference = new Paragraph(WordFieldWriter.CreateRuns("PAGEREF _Toc1 \\h", "0"));

        Add(package, reference);
        Add(package, new Paragraph(new Run(new Break { Type = BreakValues.Page })));
        Add(package, new Paragraph(new BookmarkStart { Id = "1", Name = "_Toc1" }, new Run(new Text("Late heading")), new BookmarkEnd { Id = "1" }));

        using var services = new ServiceCollection()
            .Configure<WordPreviewOptions>(options => options.MaxLayoutPages = 1)
            .BuildServiceProvider();

        await RunAsync(() =>
        {
            WordTableOfContents.RefreshAll(package, services);

            return true;
        });

        var result = WordFieldScanner.Scan(package.Body).Single(field => field.Type == "PAGEREF");

        Assert.Equal(string.Empty, string.Concat(result.ResultRuns.Select(run => run.InnerText)));
    }

    [Fact]
    public async Task PreviewWord_WithoutPages_DrawsEveryPageOfAShortDocument()
    {
        using var host = new WordToolTestHost();

        await CreateThreePageDocumentAsync(host);

        var answer = await host.InvokeAsync(new PreviewWordTool(), new { document = "pages" });
        var markers = MarkerPattern().Matches(answer).Select(match => match.Value).Distinct().ToList();

        Assert.Equal(3, markers.Count);
        Assert.Contains("Page 3 of 3", answer, StringComparison.Ordinal);

        var (_, bytes) = await host.ReadMarkerAsync(markers[2]);

        Assert.Contains("Third", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewWord_ExplicitPages_DrawsUpToRenderLimit()
    {
        using var host = new WordToolTestHost(configure: services => services.Configure<WordPreviewOptions>(options =>
        {
            options.MaxPages = 1;
            options.MaxRenderPages = 2;
        }));

        await CreateThreePageDocumentAsync(host);

        var byDefault = await host.InvokeAsync(new PreviewWordTool(), new { document = "pages" });
        var requested = await host.InvokeAsync(new PreviewWordTool(), new { document = "pages", pages = "1-2147483647" });

        Assert.Single(MarkerPattern().Matches(byDefault).Select(match => match.Value).Distinct());
        Assert.Equal(2, MarkerPattern().Matches(requested).Select(match => match.Value).Distinct().Count());
        Assert.Contains("Not shown: pages 3", requested, StringComparison.Ordinal);
    }

    private static async Task CreateThreePageDocumentAsync(WordToolTestHost host)
    {
        await host.InvokeAsync(new CreateWordDocumentTool(), new
        {
            name = "pages",
            content = new object[]
            {
                new { type = "paragraph", text = "First" },
                new { type = "page_break" },
                new { type = "paragraph", text = "Second" },
                new { type = "page_break" },
                new { type = "paragraph", text = "Third" },
            },
        });
    }

    // A regression that hangs fails the test instead of the run.
    private static Task<T> RunAsync<T>(Func<T> work)
    {
        return Task.Run(work, TestContext.Current.CancellationToken).WaitAsync(_timeout, TestContext.Current.CancellationToken);
    }

    private static void Add(WordPackage package, params OpenXmlElement[] blocks)
    {
        var section = package.Body.GetFirstChild<SectionProperties>();

        foreach (var block in blocks)
        {
            if (section is null)
            {
                package.Body.Append(block);
            }
            else
            {
                section.InsertBeforeSelf(block);
            }
        }
    }

    private static Paragraph ListItem(string text, int level)
    {
        return new Paragraph(
            new ParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = level }, new NumberingId { Val = 1 })),
            new Run(new Text(text)));
    }

    private static Paragraph Spaced(Paragraph paragraph, int before, int exactLine = 0)
    {
        var spacing = new SpacingBetweenLines { Before = before.ToString(System.Globalization.CultureInfo.InvariantCulture), After = "0" };

        if (exactLine > 0)
        {
            spacing.Line = exactLine.ToString(System.Globalization.CultureInfo.InvariantCulture);
            spacing.LineRule = LineSpacingRuleValues.Exact;
        }

        paragraph.PrependChild(new ParagraphProperties(spacing));

        return paragraph;
    }

    [GeneratedRegex(@"\[fig:\d+\]")]
    private static partial Regex MarkerPattern();
}
