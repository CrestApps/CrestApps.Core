using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Ingestion.Pdf;
using CrestApps.Core.AI.Ingestion.Pdf.Services;
using CrestApps.Core.Tests.Support;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CrestApps.Core.Tests.Core.Ingestion.Pdf;

public sealed class PdfIngestionDocumentReaderTests
{
    private const string PdfMediaType = "application/pdf";

    private const string RunningHead = "QUARTERLY REVIEW";

    [Fact]
    public async Task ReadAsync_UnsupportedMediaType_ThrowsNotSupportedException()
    {
        var reader = new PdfIngestionDocumentReader();
        await using var source = new MemoryStream();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => reader.ReadAsync(source, "file.txt", "text/plain", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_EmptyStream_ThrowsForPdfMediaType()
    {
        var reader = new PdfIngestionDocumentReader();
        await using var source = new MemoryStream();

        await Assert.ThrowsAnyAsync<Exception>(
            () => reader.ReadAsync(source, "empty.pdf", PdfMediaType, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_CorruptBytes_ThrowsForPdfMediaType()
    {
        var reader = new PdfIngestionDocumentReader();
        var garbage = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x00, 0x01, 0x02, 0xFF, 0xFE };
        await using var source = new MemoryStream(garbage);

        await Assert.ThrowsAnyAsync<Exception>(
            () => reader.ReadAsync(source, "corrupt.pdf", PdfMediaType, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_PreCanceledToken_ThrowsOperationCanceled()
    {
        var reader = new PdfIngestionDocumentReader();
        await using var source = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(source, "cancel.pdf", PdfMediaType, cts.Token));
    }

    [Fact]
    public async Task ReadAsync_HappyPath_ReadsTextFromGeneratedPdf()
    {
        var reader = new PdfIngestionDocumentReader();
        await using var source = CreatePdfWithText("Hello PDF world");

        var document = await reader.ReadAsync(source, "hello.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        var section = Assert.Single(document.Sections);
        Assert.Equal(1, section.PageNumber);
        var paragraph = Assert.IsType<Microsoft.Extensions.DataIngestion.IngestionDocumentParagraph>(
            Assert.Single(section.Elements));
        Assert.Contains("Hello PDF world", paragraph.Text);
    }

    [Fact]
    public async Task ReadAsync_SeekableSourceAtEnd_RewindsAndLeavesSourceOpen()
    {
        var reader = new PdfIngestionDocumentReader();
        await using var source = CreatePdfWithText("Seekable PDF source");
        source.Position = source.Length;

        var document = await reader.ReadAsync(
            source,
            "seekable.pdf",
            PdfMediaType,
            TestContext.Current.CancellationToken);

        var section = Assert.Single(document.Sections);
        var paragraph = Assert.IsType<Microsoft.Extensions.DataIngestion.IngestionDocumentParagraph>(
            Assert.Single(section.Elements));
        Assert.Contains("Seekable PDF source", paragraph.Text);

        source.Position = 0;
        Assert.Equal((byte)'%', source.ReadByte());
    }

    /// <summary>
    /// Verifies that a two column page reads down one column and then the other, even though the content
    /// stream draws the columns interleaved.
    /// </summary>
    [Fact]
    public async Task ReadAsync_TwoColumnPage_EmitsColumnsInReadingOrder()
    {
        var reader = CreateReader();
        await using var source = new PdfFixtureBuilder()
            .Page(page => page
                .Text("Right top alpha. ", 320, 700, 11)
                .Text("Left top bravo. ", 40, 700, 11)
                .Text("Right bottom charlie. ", 320, 682, 11)
                .Text("Left bottom delta. ", 40, 682, 11))
            .Build();

        var document = await reader.ReadAsync(source, "columns.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        var text = Flatten(document);

        Assert.True(
            text.IndexOf("Left bottom delta", StringComparison.Ordinal) < text.IndexOf("Right top alpha", StringComparison.Ordinal),
            $"Columns were not read in order. Got: {text}");
    }

    /// <summary>
    /// Verifies that a line repeated at the top of every page is marked as decoration, so nothing that embeds
    /// text sees it, while the body text of each page survives. The line itself is kept on the page, marked,
    /// because structure analysis reads running heads and printed page numbers from exactly these blocks.
    /// </summary>
    [Fact]
    public async Task ReadAsync_RepeatedRunningHead_IsMarkedAsDecorationOnEveryPage()
    {
        var reader = CreateReader();
        var builder = new PdfFixtureBuilder();

        for (var index = 0; index < 5; index++)
        {
            var pageIndex = index;

            builder.Page(page =>
            {
                page.Text(RunningHead + " ", 40, 800, 8);
                AddBody(page, pageIndex);
            });
        }

        await using var source = builder.Build();

        var document = await reader.ReadAsync(source, "heads.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        var text = Flatten(document);

        Assert.DoesNotContain(RunningHead, text, StringComparison.Ordinal);

        for (var index = 0; index < 5; index++)
        {
            Assert.Contains($"Body paragraph {index}", text, StringComparison.Ordinal);
        }

        var decoration = document.EnumerateContent().Where(element => element.IsDecoration()).ToList();

        Assert.Equal(5, decoration.Count);
        Assert.All(decoration, element =>
        {
            Assert.IsType<IngestionDocumentHeader>(element);
            Assert.Contains(RunningHead, element.Text, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Verifies that a host can still drop decoration outright, which is the escape hatch when a corpus has
    /// no use for running heads at all.
    /// </summary>
    [Fact]
    public async Task ReadAsync_DecorationEmissionDisabled_DropsRunningHead()
    {
        var reader = CreateReader(options => options.EmitDecorationAsHeaderFooter = false);
        var builder = new PdfFixtureBuilder();

        for (var index = 0; index < 5; index++)
        {
            var pageIndex = index;

            builder.Page(page =>
            {
                page.Text(RunningHead + " ", 40, 800, 8);
                AddBody(page, pageIndex);
            });
        }

        await using var source = builder.Build();

        var document = await reader.ReadAsync(source, "heads.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(document.EnumerateContent(), element => element.Text.Contains(RunningHead, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that a page holding one short line keeps it. Dropping it would leave the page with nothing at
    /// all, which is never an improvement over keeping a running head.
    /// </summary>
    [Fact]
    public async Task ReadAsync_DecorationGuard_KeepsOnlyBlockOnPage()
    {
        var reader = CreateReader();
        var builder = new PdfFixtureBuilder();

        for (var index = 0; index < 4; index++)
        {
            var pageIndex = index;

            builder.Page(page =>
            {
                page.Text(RunningHead + " ", 40, 800, 8);
                AddBody(page, pageIndex);
            });
        }

        builder.Page(page => page.Text(RunningHead + " ", 40, 800, 8));

        await using var source = builder.Build();

        var document = await reader.ReadAsync(source, "sparse.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        var lastSection = Assert.Single(document.Sections, section => section.PageNumber == 5);
        var element = Assert.Single(lastSection.Elements);

        Assert.Contains(RunningHead, element.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that boilerplate repeated on every page survives. A block that long, and made of whole
    /// sentences, is prose that happens to repeat, not page furniture.
    /// </summary>
    [Fact]
    public async Task ReadAsync_DecorationGuard_KeepsLongRepeatedBlock()
    {
        const string boilerplate =
            "This notice repeats on every page of the publication. It states the terms under which the " +
            "material may be reproduced. It also names the body responsible for the content. Readers are " +
            "asked to consult the full terms before reuse. ";

        var reader = CreateReader();
        var builder = new PdfFixtureBuilder();

        for (var index = 0; index < 3; index++)
        {
            var pageIndex = index;

            builder.Page(page =>
            {
                page.Text(boilerplate, 40, 815, 6);
                AddBody(page, pageIndex);
            });
        }

        await using var source = builder.Build();

        var document = await reader.ReadAsync(source, "boilerplate.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        Assert.Equal(3, document.Sections.Count);

        foreach (var section in document.Sections)
        {
            var sectionText = string.Join('\n', section.Elements.Select(element => element.Text));

            Assert.Contains("This notice repeats on every page", sectionText, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies that every emitted element carries both its text and the page it came from. An element whose
    /// text was never assigned is invisible to every consumer, and a page number set only on the section is
    /// invisible because content enumeration does not yield sections.
    /// </summary>
    [Fact]
    public async Task ReadAsync_EveryElementHasTextAndPageNumber()
    {
        var reader = CreateReader();
        await using var source = new PdfFixtureBuilder()
            .Page(page => page
                .Text("Alpha bravo charlie. ", 40, 760, 11)
                .Text("Delta echo foxtrot. ", 40, 742, 11))
            .Page(page => page
                .Text("Golf hotel india. ", 40, 760, 11))
            .Build();

        var document = await reader.ReadAsync(source, "pages.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        var elements = document.EnumerateContent().ToList();

        Assert.NotEmpty(elements);
        Assert.All(elements, element =>
        {
            Assert.True(element.PageNumber.HasValue, "An element carried no page number.");
            Assert.False(string.IsNullOrWhiteSpace(element.Text), "An element carried no text.");
        });

        Assert.Contains(elements, element => element.PageNumber == 2);
    }

    /// <summary>
    /// Verifies that a ligature extracted out of a PDF is expanded to the letters it stands for, so a search
    /// for the word finds it, and that the printed numbers around it survive untouched.
    /// </summary>
    /// <remarks>
    /// The superscript half of this case is asserted in <see cref="PdfTextNormalizerTests"/> instead: the
    /// Standard 14 fonts the fixture writer uses do not round-trip U+00B2, so a generated fixture cannot
    /// carry one. That is a limitation of writing synthetic PDFs, not of reading real ones.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_LigatureWord_IsNormalized()
    {
        var reader = CreateReader();
        await using var source = new PdfFixtureBuilder()
            .Page(page => page
                .Text("proﬁ lokkal javul a hatasfok. ", 40, 760, 11)
                .Text("A meredekseg y = 1,0451x, a josag 0,9412. ", 40, 742, 11))
            .Build();

        var document = await reader.ReadAsync(source, "ligature.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        var text = Flatten(document);

        Assert.Contains("profilokkal", text, StringComparison.Ordinal);
        Assert.Contains("y = 1,0451x", text, StringComparison.Ordinal);
        Assert.Contains("0,9412", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that layout analysis can be turned off entirely, reproducing the raw content-stream text one
    /// paragraph per page. This is the escape hatch when segmentation misbehaves on a corpus.
    /// </summary>
    [Fact]
    public async Task ReadAsync_LayoutAnalysisDisabled_MatchesLegacyOutput()
    {
        var bytes = new PdfFixtureBuilder()
            .Page(page => page
                .Text("Right top alpha. ", 320, 700, 11)
                .Text("Left top bravo. ", 40, 700, 11))
            .Page(page => page
                .Text("Golf hotel india. ", 40, 760, 11))
            .BuildBytes();

        var reader = CreateReader(options => options.UseLayoutAnalysis = false);
        await using var source = new MemoryStream(bytes);

        var document = await reader.ReadAsync(source, "legacy.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        using var pdf = PdfDocument.Open(bytes);

        Assert.Equal(pdf.NumberOfPages, document.Sections.Count);

        for (var pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
        {
            var section = document.Sections[pageNumber - 1];
            var paragraph = Assert.IsType<IngestionDocumentParagraph>(Assert.Single(section.Elements));

            Assert.Equal(pageNumber, section.PageNumber);
            Assert.Equal(pdf.GetPage(pageNumber).Text.Trim(), paragraph.Text);
        }
    }

    /// <summary>
    /// Verifies that a figure is emitted with its bytes, its bounds and a hash of its content, and that it
    /// carries no text of its own until something has described it.
    /// </summary>
    [Fact]
    public async Task ReadAsync_PageWithPngImage_EmitsImageElementWithHashAndBounds()
    {
        var reader = CreateReader();
        await using var source = new PdfFixtureBuilder()
            .Page(page => page
                .Text("Alpha bravo charlie. ", 40, 760, 11)
                .Png(TestImageFactory.BarChart(200, 120), 40, 560, 200, 120))
            .Build();

        var document = await reader.ReadAsync(source, "chart.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        var image = Assert.Single(document.EnumerateContent().OfType<IngestionDocumentImage>());

        Assert.Equal("chart.pdf-p1-1", image.GetFigureId());
        Assert.Equal(1, image.PageNumber);
        Assert.Null(image.Text);
        Assert.True(image.Content.HasValue);
        Assert.NotEmpty(image.Content.Value.ToArray());

        var hash = image.GetMetadataString(FigureMetadataKeys.ContentHash);

        Assert.Equal(64, hash?.Length);

        var bounds = Assert.IsType<double[]>(image.Metadata[ElementMetadataKeys.BoundingBox]);

        Assert.Equal(4, bounds.Length);
        Assert.Equal(40d, bounds[0], 1);
        Assert.Equal(560d, bounds[1], 1);
        Assert.Equal(200, Assert.IsType<int>(image.Metadata[FigureMetadataKeys.PixelWidth]));
        Assert.Equal(120, Assert.IsType<int>(image.Metadata[FigureMetadataKeys.PixelHeight]));
    }

    /// <summary>
    /// Verifies that the same artwork placed twice is recorded once as a figure and once as a repeat of it.
    /// </summary>
    [Fact]
    public async Task ReadAsync_SameImageTwice_SecondIsMarkedDuplicate()
    {
        var chart = TestImageFactory.BarChart(200, 120);
        var reader = CreateReader();
        await using var source = new PdfFixtureBuilder()
            .Page(page => page
                .Text("Alpha bravo charlie. ", 40, 760, 11)
                .Png(chart, 40, 560, 200, 120))
            .Page(page => page
                .Text("Delta echo foxtrot. ", 40, 760, 11)
                .Png(chart, 40, 560, 200, 120))
            .Build();

        var document = await reader.ReadAsync(source, "repeat.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        var images = document.EnumerateContent().OfType<IngestionDocumentImage>().ToList();

        Assert.Equal(2, images.Count);
        Assert.Null(images[0].GetMetadataString(FigureMetadataKeys.DuplicateOf));
        Assert.Equal(images[0].GetFigureId(), images[1].GetMetadataString(FigureMetadataKeys.DuplicateOf));
        Assert.Equal(
            images[0].GetMetadataString(FigureMetadataKeys.ContentHash),
            images[1].GetMetadataString(FigureMetadataKeys.ContentHash));
    }

    /// <summary>
    /// Verifies that an image too small to be a figure is left out. At that size it is a rule, a bullet or an
    /// icon, never something worth storing or describing.
    /// </summary>
    [Fact]
    public async Task ReadAsync_TinyImage_IsSkipped()
    {
        var reader = CreateReader();
        await using var source = new PdfFixtureBuilder()
            .Page(page => page
                .Text("Alpha bravo charlie. ", 40, 760, 11)
                .Png(TestImageFactory.Solid(8, 8, (10, 10, 10)), 40, 700, 8, 8))
            .Build();

        var document = await reader.ReadAsync(source, "tiny.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        Assert.Empty(document.EnumerateContent().OfType<IngestionDocumentImage>());
    }

    /// <summary>
    /// Verifies that image emission can be turned off entirely.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ImagesDisabled_EmitsNoImages()
    {
        var reader = CreateReader(options => options.EmitImages = false);
        await using var source = new PdfFixtureBuilder()
            .Page(page => page
                .Text("Alpha bravo charlie. ", 40, 760, 11)
                .Png(TestImageFactory.BarChart(200, 120), 40, 560, 200, 120))
            .Build();

        var document = await reader.ReadAsync(source, "disabled.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        Assert.Empty(document.EnumerateContent().OfType<IngestionDocumentImage>());
    }

    /// <summary>
    /// Verifies that block geometry and typography travel with each element so a later processor can decide
    /// what a block is without re-reading the file.
    /// </summary>
    [Fact]
    public async Task ReadAsync_Element_CarriesBoundsAndTypography()
    {
        var reader = CreateReader();
        await using var source = new PdfFixtureBuilder()
            .Page(page => page.Text("Alpha bravo charlie. ", 40, 760, 11))
            .Build();

        var document = await reader.ReadAsync(source, "typography.pdf", PdfMediaType, TestContext.Current.CancellationToken);

        var element = Assert.Single(document.EnumerateContent());

        Assert.True(element.HasMetadata);

        var bounds = Assert.IsType<double[]>(element.Metadata[ElementMetadataKeys.BoundingBox]);

        Assert.Equal(4, bounds.Length);
        Assert.Equal(11d, Assert.IsType<double>(element.Metadata[ElementMetadataKeys.ModalPointSize]), 1);
        Assert.False(string.IsNullOrEmpty(element.GetMetadataString(ElementMetadataKeys.ModalFontName)));
    }

    private static PdfIngestionDocumentReader CreateReader(Action<PdfLayoutOptions> configure = null)
    {
        var options = new PdfLayoutOptions();

        configure?.Invoke(options);

        return new PdfIngestionDocumentReader(Options.Create(options));
    }

    /// <summary>
    /// Draws a body paragraph of ordinary density. A page carrying one lonely line gives page segmentation
    /// nothing to estimate line spacing from, and it merges everything on the page into one block; a real
    /// page has many lines and a fixture has to as well for the test to mean anything.
    /// </summary>
    /// <param name="page">The page being drawn.</param>
    /// <param name="pageIndex">The zero-based page index, which makes each page's text distinct.</param>
    private static void AddBody(PdfFixturePage page, int pageIndex)
    {
        page.Text($"Body paragraph {pageIndex} sierra tango uniform. ", 40, 700, 11);

        for (var line = 1; line < 7; line++)
        {
            page.Text($"Continuation line {line} victor whiskey xray yankee zulu. ", 40, 700 - (line * 14), 11);
        }
    }

    /// <summary>
    /// Joins the text every consumer of the document would embed: everything except page furniture, which
    /// is kept on the page for structure analysis and skipped by everything that embeds text.
    /// </summary>
    /// <param name="document">The read document.</param>
    /// <returns>The joined text.</returns>
    private static string Flatten(IngestionDocument document)
    {
        return string.Join('\n', document.EnumerateContent().Where(element => !element.IsDecoration()).Select(element => element.Text));
    }

    private static MemoryStream CreatePdfWithText(string text)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText(text, 12, new PdfPoint(25, 700), font);

        var bytes = builder.Build();

        return new MemoryStream(bytes);
    }
}
