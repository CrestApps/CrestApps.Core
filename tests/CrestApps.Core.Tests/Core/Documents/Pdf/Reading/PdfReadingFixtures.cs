using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Graphics;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;
using CrestApps.Core.AI.Documents.Pdf.Objects;
using Microsoft.Extensions.Options;
using PdfSharp.Pdf.IO;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

/// <summary>
/// Builds the PDFs the reading tool tests run against, in code, so no fixture files are needed.
/// </summary>
internal static class PdfReadingFixtures
{
    private static readonly PdfBrush _black = new(new PdfColor(0, 0, 0));
    private static readonly PdfBrush _gray = new(new PdfColor(0x80, 0x80, 0x80));

    /// <summary>
    /// Builds a three-page composed report: headings, paragraphs, a list, a table, a picture with a caption,
    /// and a heading on every page.
    /// </summary>
    /// <param name="language">The language the document declares.</param>
    /// <returns>The PDF file.</returns>
    public static async Task<byte[]> ReportAsync(string language = "en-US")
    {
        var definition = new PdfDocumentDefinition
        {
            Title = "Quarterly Report",
            Author = "Contoso Analytics",
            Language = language,
            Sections =
            [
                new PdfSectionDefinition
                {
                    Id = "s1",
                    Blocks =
                    [
                        new PdfBlockDefinition { Id = "b1", Type = "heading", Text = "Quarterly Report", Level = 1 },
                        new PdfBlockDefinition { Id = "b2", Type = "paragraph", Text = "Revenue rose by twelve percent against the plan for the quarter, and the growth was spread across every region that the company serves." },
                        new PdfBlockDefinition { Id = "b3", Type = "heading", Text = "Regional results", Level = 2 },
                        new PdfBlockDefinition { Id = "b4", Type = "list", Items = ["North grew faster than expected", "South held flat"] },
                        new PdfBlockDefinition
                        {
                            Id = "b5",
                            Type = "table",
                            Table = new PdfTableDefinition
                            {
                                Columns =
                                [
                                    new PdfTableColumnDefinition { Header = "Region" },
                                    new PdfTableColumnDefinition { Header = "Revenue" },
                                    new PdfTableColumnDefinition { Header = "Share" },
                                ],
                                Rows =
                                [
                                    ["North", "74612", "25%"],
                                    ["South", "1200", "75%"],
                                    ["East", "5300", "10%"],
                                ],
                            },
                        },
                        new PdfBlockDefinition { Id = "b6", Type = "page_break" },
                        new PdfBlockDefinition { Id = "b7", Type = "heading", Text = "Outlook", Level = 1 },
                        new PdfBlockDefinition { Id = "b8", Type = "paragraph", Text = "The outlook for the next quarter is positive, and the revenue forecast has been raised for all of the regions." },
                        new PdfBlockDefinition { Id = "b9", Type = "image", Image = new PdfImageDefinition { Source = "logo.png", Caption = "Figure 1: Company logo", Width = 120 } },
                        new PdfBlockDefinition { Id = "b10", Type = "page_break" },
                        new PdfBlockDefinition { Id = "b11", Type = "heading", Text = "Appendix", Level = 1 },
                        new PdfBlockDefinition { Id = "b12", Type = "paragraph", Text = "This appendix lists the methods that were used to compute the revenue figures in this report." },
                    ],
                },
            ],
        };

        return await ComposeAsync(definition);
    }

    /// <summary>
    /// Renders a definition, resolving <c>logo.png</c> to a red square.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The PDF file.</returns>
    public static async Task<byte[]> ComposeAsync(PdfDocumentDefinition definition)
    {
        var composer = new PdfDocumentComposer(Options.Create(new PdfCompositionOptions()), TimeProvider.System);
        var images = new DictionaryImageSource(new Dictionary<string, PdfImageData>
        {
            ["logo.png"] = new PdfImageData(PdfTestImages.SolidPng(64, 48, 0xE0, 0x20, 0x20), "image/png", "logo.png"),
        });

        var result = await composer.ComposeAsync(definition, images, TestContext.Current.CancellationToken);

        return result.Bytes;
    }

    /// <summary>
    /// Builds a composed document of plain paragraphs, one per entry.
    /// </summary>
    /// <param name="paragraphs">The paragraphs.</param>
    /// <returns>The PDF file.</returns>
    public static Task<byte[]> ParagraphsAsync(params string[] paragraphs)
    {
        var blocks = new List<PdfBlockDefinition>();

        for (var index = 0; index < paragraphs.Length; index++)
        {
            blocks.Add(paragraphs[index] == "---"
                ? new PdfBlockDefinition { Id = "b" + index, Type = "page_break" }
                : new PdfBlockDefinition { Id = "b" + index, Type = "paragraph", Text = paragraphs[index] });
        }

        return ComposeAsync(new PdfDocumentDefinition
        {
            Sections = [new PdfSectionDefinition { Id = "s1", Blocks = blocks }],
        });
    }

    /// <summary>
    /// Builds a two-page PDF drawn page by page: a web link, a mail link, a script link, a link to page 2, a
    /// sticky note, bookmarks and a declared language.
    /// </summary>
    /// <returns>The PDF file.</returns>
    public static byte[] LinksAndBookmarks()
    {
        using var document = new PdfDocument();
        var font = PdfFontLibrary.Get("Arial");
        var large = PdfFontLibrary.Get("Arial", bold: true);

        var first = LetterPage(document);
        var second = LetterPage(document);

        var canvas = PdfCanvas.ForPage(first);
        canvas.DrawText("Introduction", large, 20, 72, 90, _black);
        canvas.DrawText("Visit our website for details.", font, 12, 72, 140, _black);
        canvas.DrawText("Write to the team.", font, 12, 72, 170, _black);
        canvas.DrawText("Run the script.", font, 12, 72, 200, _black);
        canvas.DrawText("Go to the appendix.", font, 12, 72, 230, _black);
        canvas.Commit();

        canvas = PdfCanvas.ForPage(second);
        canvas.DrawText("Appendix", large, 20, 72, 90, _black);
        canvas.DrawText("The appendix holds the details.", font, 12, 72, 140, _black);
        canvas.Commit();

        // Annotation rectangles are in PDF user space: the origin is the bottom-left corner of the page.
        var height = first.Height;

        PdfFixtureObjects.AddWebLink(first, 72, height - 145, 200, 18, "https://example.com/details");
        PdfFixtureObjects.AddWebLink(first, 72, height - 175, 150, 18, "mailto:team@example.com");
        PdfFixtureObjects.AddWebLink(first, 72, height - 205, 150, 18, "javascript:alert(1)");
        PdfFixtureObjects.AddPageLink(first, 72, height - 235, 160, 18, second);
        PdfFixtureObjects.AddNote(first, 400, height - 100, 20, 20, "Reviewer", "Check the figures");

        var introduction = PdfFixtureObjects.AddBookmark(document, null, "Introduction", first, open: true);
        PdfFixtureObjects.AddBookmark(document, introduction, "Links", first);
        PdfFixtureObjects.AddBookmark(document, null, "Appendix", second, open: true);

        document.Info.Title = "Links sample";
        document.Info.Author = "Tester";
        document.Language = "en-GB";

        return document.Save();
    }

    /// <summary>
    /// Builds a three-page PDF drawn page by page, each page set in two columns under a full-width heading,
    /// with a running head at the top and a page number at the bottom of every page.
    /// </summary>
    /// <returns>The PDF file.</returns>
    public static byte[] TwoColumnsWithRunningText()
    {
        using var document = new PdfDocument();
        var body = PdfFontLibrary.Get("Arial");
        var heading = PdfFontLibrary.Get("Arial", bold: true);

        string[] left =
        [
            "The market grew steadily during the year,",
            "helped by lower costs and strong demand",
            "from new customers in every region. Sales",
            "teams opened offices in three new cities",
            "and the product range was extended with",
            "two services that customers had asked for.",
            "Margins improved as the cost of materials",
            "fell back from the peak of the prior year.",
        ];

        string[] right =
        [
            "Looking ahead, the board expects growth",
            "to continue at a slower pace next year as",
            "competition increases in the largest",
            "markets. Investment will focus on service",
            "quality and on the systems that support",
            "the sales teams in the field. A review of",
            "pricing is planned for the first quarter",
            "and its results will be shared in spring.",
        ];

        string[] headings = ["Market overview", "Regional detail", "Outlook and risks"];

        for (var number = 1; number <= headings.Length; number++)
        {
            var canvas = PdfCanvas.ForPage(document.AddPage());

            canvas.DrawText("Northwind Annual Review", body, 8, 60, 36, _gray);
            canvas.DrawText(headings[number - 1], heading, 18, 60, 110, _black);

            for (var line = 0; line < left.Length; line++)
            {
                canvas.DrawText(left[line], body, 10, 60, 150 + (line * 14), _black);
                canvas.DrawText(right[line], body, 10, 320, 150 + (line * 14), _black);
            }

            canvas.DrawText("Page " + number.ToString(CultureInfo.InvariantCulture), body, 8, 280, 810, _gray);
            canvas.Commit();
        }

        return document.Save();
    }

    /// <summary>
    /// Builds a composed document holding one long table that runs over two pages, its header repeated on
    /// the second.
    /// </summary>
    /// <param name="rows">The number of data rows.</param>
    /// <returns>The PDF file.</returns>
    public static Task<byte[]> LongTableAsync(int rows = 70)
    {
        var data = new List<List<string>>();

        for (var index = 1; index <= rows; index++)
        {
            data.Add(["Item " + index.ToString(CultureInfo.InvariantCulture), (index * 10).ToString(CultureInfo.InvariantCulture), index % 2 == 0 ? "Even" : "Odd"]);
        }

        return ComposeAsync(new PdfDocumentDefinition
        {
            Sections =
            [
                new PdfSectionDefinition
                {
                    Id = "s1",
                    Blocks =
                    [
                        new PdfBlockDefinition { Id = "b1", Type = "heading", Text = "Inventory", Level = 1 },
                        new PdfBlockDefinition
                        {
                            Id = "b2",
                            Type = "table",
                            Table = new PdfTableDefinition
                            {
                                Columns =
                                [
                                    new PdfTableColumnDefinition { Header = "Item" },
                                    new PdfTableColumnDefinition { Header = "Quantity" },
                                    new PdfTableColumnDefinition { Header = "Parity" },
                                ],
                                Rows = data,
                            },
                        },
                    ],
                },
            ],
        });
    }

    /// <summary>
    /// Builds a PDF whose first page holds the given lines of text and whose second page is blank, as a
    /// scanned page without a text layer reads.
    /// </summary>
    /// <param name="lines">The lines drawn on the first page.</param>
    /// <returns>The PDF file.</returns>
    public static byte[] TextAndBlankPage(params string[] lines)
    {
        using var document = new PdfDocument();
        var font = PdfFontLibrary.Get("Arial");
        var canvas = PdfCanvas.ForPage(LetterPage(document));

        for (var index = 0; index < lines.Length; index++)
        {
            canvas.DrawText(lines[index], font, 11, 72, 100 + (index * 16), _black);
        }

        canvas.Commit();
        LetterPage(document);

        return document.Save();
    }

    /// <summary>
    /// Protects a PDF with passwords.
    /// </summary>
    /// <param name="pdf">The PDF file.</param>
    /// <param name="userPassword">The password that opens it.</param>
    /// <param name="ownerPassword">The password that allows changes.</param>
    /// <returns>The protected file.</returns>
    public static byte[] Protect(byte[] pdf, string userPassword, string ownerPassword)
    {
        using var input = new MemoryStream(pdf);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        document.SecuritySettings.UserPassword = userPassword;
        document.SecuritySettings.OwnerPassword = ownerPassword;

        using var buffer = new MemoryStream();
        document.Save(buffer, closeStream: false);

        return buffer.ToArray();
    }

    private static PdfPage LetterPage(PdfDocument document)
    {
        var page = document.AddPage();

        page.MediaBox = new PdfRectangle(0, 0, 612, 792);

        return page;
    }

    /// <summary>
    /// Resolves pictures from a dictionary.
    /// </summary>
    private sealed class DictionaryImageSource : IPdfImageSource
    {
        private readonly Dictionary<string, PdfImageData> _images;

        /// <summary>
        /// Initializes a new instance of the <see cref="DictionaryImageSource"/> class.
        /// </summary>
        /// <param name="images">The pictures by source.</param>
        public DictionaryImageSource(Dictionary<string, PdfImageData> images)
        {
            _images = images;
        }

        /// <summary>
        /// Resolves a picture.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The picture, or <see langword="null"/>.</returns>
        public Task<PdfImageData> ResolveAsync(string source, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_images.TryGetValue(source, out var image) ? image : null);
        }
    }
}
