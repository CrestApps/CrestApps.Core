using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Intelligence;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Reads the text of scanned pages with a vision model and, on request, adds it to the file as an invisible
/// layer so the PDF becomes searchable.
/// </summary>
internal sealed class OcrPdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.OcrPdf;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            "pages": {
              "type": "string",
              "description": "Optional page selection, one-based: \"3\", \"1-3,5\", \"7-\", \"all\". Omit to read every page that has a scanned image but no extractable text."
            },
            {{PdfToolSchemas.Password}},
            "make_searchable": {
              "type": "boolean",
              "description": "Also save a working copy with the recognised text as an invisible layer on the scanned pages, so the PDF can be searched and copied from. Defaults to false."
            },
            {{PdfToolSchemas.SaveAs}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    private const string Instructions = """
        You are an OCR engine. Transcribe every piece of text visible in the image exactly as written, in natural reading order: top to bottom, and for several columns finish each column before starting the next.
        - Keep each printed line on its own line, with a blank line between paragraphs.
        - Write tables as Markdown tables.
        - Copy numbers, dates, codes, names and punctuation exactly as printed; never correct, translate or complete them.
        - Write [illegible] for text you cannot read.
        - Do not describe pictures, add commentary or summarize.
        - If the image contains no text at all, reply exactly: [no text]
        """;

    private const string NoText = "[no text]";

    /// <summary>
    /// Initializes a new instance of the <see cref="OcrPdfTool"/> class.
    /// </summary>
    public OcrPdfTool()
        : base(Schema)
    {
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Reads the text of scanned or image-only PDF pages with a vision model (OCR) and returns it page by page, tables as Markdown. By default it reads every page that shows a scanned image but has no extractable text; pass 'pages' to choose. Reads a limited number of pages per call and says which remain. With make_searchable it also saves a working copy whose scanned pages carry the text as an invisible layer, so the PDF can be searched. Use extract_pdf_text first: pages that already have text do not need OCR.";

    /// <summary>
    /// Recognises the text of scanned pages.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The recognised text.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        var password = arguments.GetString("password");
        var requested = arguments.GetPages();
        var limit = Math.Max(1, context.Options.MaxVisionPagesPerCall);

        List<int> pages;
        var textless = new HashSet<int>();
        var scans = new List<OcrPage>();

        using (var pdf = PdfFiles.OpenForReading(bytes, password))
        {
            if (requested is null)
            {
                pages = [];

                for (var number = 1; number <= pdf.NumberOfPages; number++)
                {
                    var page = pdf.GetPage(number);

                    if (PdfCorpus.IsNearlyEmpty(PdfPageText.GetPlainText(page)))
                    {
                        textless.Add(number);

                        if (PdfPageImages.Get(page).Count > 0)
                        {
                            pages.Add(number);
                        }
                    }
                }

                if (pages.Count == 0)
                {
                    return $"No page of {source.Describe()} is a scanned image without text, so there is nothing to OCR. Read its text with extract_pdf_text, or pass 'pages' to OCR particular pages anyway.";
                }
            }
            else
            {
                pages = PdfPageRange.Parse(requested, pdf.NumberOfPages);
            }

            foreach (var number in pages.Take(limit))
            {
                var page = pdf.GetPage(number);

                if (requested is not null && PdfCorpus.IsNearlyEmpty(PdfPageText.GetPlainText(page)))
                {
                    textless.Add(number);
                }

                scans.Add(Prepare(page, number, context.Options.MaxImageBytes));
            }
        }

        var remaining = pages.Skip(limit).ToList();
        var model = await PdfModelClient.ResolveVisionAsync(context.Services, cancellationToken);

        if (model is null)
        {
            return $"No vision-capable AI model is configured on this host, so the scanned pages of {source.Describe()} cannot be read. Pages that need OCR: {PdfPageRange.Describe(pages)}. Tell the user, and suggest uploading a PDF that has a text layer.";
        }

        var readable = scans.Where(scan => scan.Image is not null).ToList();
        var answers = await PdfModelClient.MapAsync(
            readable,
            (scan, token) => model.DescribeImageAsync(
                Instructions,
                string.Create(CultureInfo.InvariantCulture, $"Transcribe page {scan.Page} of \"{source.Name}\"."),
                scan.Image,
                scan.MediaType,
                4000,
                token),
            cancellationToken);

        for (var index = 0; index < readable.Count; index++)
        {
            readable[index].Text = answers[index];
        }

        var builder = new StringBuilder();

        builder.Append("OCR of ").Append(source.Describe()).Append(" by a vision model, ")
            .Append(scans.Count.ToString(CultureInfo.InvariantCulture)).Append(" page(s): ")
            .Append(PdfPageRange.Describe(scans.Select(scan => scan.Page))).Append(".\n");

        foreach (var scan in scans)
        {
            builder.Append("\n## Page ").Append(scan.Page.ToString(CultureInfo.InvariantCulture)).Append('\n');

            if (scan.Image is null)
            {
                builder.Append("(Not read: ").Append(scan.Problem).Append(".)\n");
            }
            else if (IsEmpty(scan.Text))
            {
                builder.Append("(The model found no text on this page.)\n");
            }
            else
            {
                builder.Append(scan.Text.Trim()).Append('\n');
            }
        }

        if (remaining.Count > 0)
        {
            builder.Append("\nNot read yet (at most ").Append(limit.ToString(CultureInfo.InvariantCulture))
                .Append(" pages per call): pages ").Append(PdfPageRange.Describe(remaining))
                .Append(". Call ocr_pdf again with pages \"").Append(PdfPageRange.Describe(remaining).Replace(" ", string.Empty, StringComparison.Ordinal)).Append("\" to read them.\n");
        }

        if (arguments.GetBoolean("make_searchable") == true)
        {
            builder.Append('\n').Append(await MakeSearchableAsync(arguments, context, source, password, scans, textless, cancellationToken)).Append('\n');
        }

        return builder.ToString().TrimEnd();
    }

    private static OcrPage Prepare(Page page, int number, int maxBytes)
    {
        var scan = new OcrPage { Page = number };
        var images = PdfPageImages.Get(page);

        if (images.Count == 0)
        {
            scan.Problem = "the page has no embedded image to read; it may be drawn with vector graphics, so use preview_pdf to look at it";

            return scan;
        }

        // A scan is the largest picture on its page; logos and stamps drawn over it are smaller.
        foreach (var image in images.OrderByDescending(PdfPageImages.Area))
        {
            if (PdfPageImages.TryEncode(image, maxBytes, out var encoded, out var mediaType, out var problem))
            {
                scan.Image = encoded;
                scan.MediaType = mediaType;
                scan.Problem = null;

                return scan;
            }

            scan.Problem ??= problem;
        }

        return scan;
    }

    private static async Task<string> MakeSearchableAsync(
        PdfToolArguments arguments,
        PdfToolContext context,
        PdfSource source,
        string password,
        List<OcrPage> scans,
        HashSet<int> textless,
        CancellationToken cancellationToken)
    {
        var layered = scans
            .Where(scan => textless.Contains(scan.Page) && !IsEmpty(scan.Text))
            .ToList();

        var skipped = scans
            .Where(scan => !textless.Contains(scan.Page) && !IsEmpty(scan.Text))
            .Select(scan => scan.Page)
            .ToList();

        if (layered.Count == 0)
        {
            return skipped.Count > 0
                ? $"No searchable copy was saved: pages {PdfPageRange.Describe(skipped)} already have their own text, so a second layer would only duplicate it."
                : "No searchable copy was saved: no text was recognised on a page that lacks it.";
        }

        try
        {
            var working = await context.MutateAsync(
                async state =>
                {
                    // An upload is found again by its id, which no working PDF can share.
                    var target = context.FindPdf(state, source.IsUpload ? source.Upload.ItemId : source.Name);
                    var current = await context.ReadPdfAsync(target, cancellationToken);

                    using var document = PdfFiles.OpenForEditing(current, password);

                    foreach (var scan in layered)
                    {
                        if (scan.Page <= document.PageCount)
                        {
                            PdfTextLayerWriter.AddInvisibleText(document.Pages[scan.Page - 1], scan.Text);
                        }
                    }

                    var edited = PdfFiles.Save(document);

                    return await context.SaveWorkingFileAsync(
                        state,
                        target,
                        arguments.GetString("save_as"),
                        edited,
                        $"Added a searchable OCR text layer to pages {PdfPageRange.Describe(layered.Select(scan => scan.Page))}",
                        cancellationToken);
                },
                cancellationToken);

            var note = skipped.Count > 0
                ? $" Pages {PdfPageRange.Describe(skipped)} already had text and were left as they were."
                : string.Empty;

            return $"Saved working PDF \"{working.Name}\" with an invisible, searchable text layer on pages {PdfPageRange.Describe(layered.Select(scan => scan.Page))}. The page images are unchanged. The layer's lines are spread evenly down each page, so searching and copying work but a highlight lands only approximately over the printed words.{note}";
        }
        catch (PdfToolException ex)
        {
            return "No searchable copy was saved: " + ex.Message;
        }
    }

    private static bool IsEmpty(string text)
    {
        return string.IsNullOrWhiteSpace(text) || text.Trim().Equals(NoText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One page an OCR call reads.
    /// </summary>
    private sealed class OcrPage
    {
        /// <summary>
        /// Gets the one-based page number.
        /// </summary>
        public int Page { get; init; }

        /// <summary>
        /// Gets or sets the picture sent to the model, or <see langword="null"/> when there is none to send.
        /// </summary>
        public byte[] Image { get; set; }

        /// <summary>
        /// Gets or sets the picture's media type.
        /// </summary>
        public string MediaType { get; set; }

        /// <summary>
        /// Gets or sets why the page could not be read.
        /// </summary>
        public string Problem { get; set; }

        /// <summary>
        /// Gets or sets the text the model read.
        /// </summary>
        public string Text { get; set; }
    }
}
