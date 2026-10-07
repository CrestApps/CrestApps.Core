using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Intelligence;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Describes the pictures embedded in a PDF — photographs, figures, charts and diagrams — with a vision
/// model, quoting the values a chart prints.
/// </summary>
internal sealed class AnalyzePdfImagesTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.AnalyzePdfImages;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "images": {
              "type": "array",
              "items": { "type": "integer" },
              "description": "Optional image numbers to describe, numbered from 1 across the whole document in page order, as extract_pdf_images lists them. Omit to describe the images on the selected pages."
            },
            "question": {
              "type": "string",
              "description": "Optional question to answer about each image, for example \"What is the value for 2023?\". Omit for a general description."
            }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    private const string Instructions = """
        You describe pictures taken from a PDF, precisely and concisely.
        - For a chart or graph: name its type, title, axes and legend, then list every value and label printed on it, verbatim. Never estimate a value that is not printed; say it is not labelled instead.
        - For a table: transcribe it as a Markdown table.
        - For a diagram: describe its parts and how they connect, and transcribe its text.
        - For a photograph or illustration: say what it shows in two or three sentences, and transcribe any text in it.
        - When a question is asked, answer it first, using only what the picture shows.
        Never guess at information the picture does not contain.
        """;

    // Rules, bullets and tiny logos are drawn as images too; they are not worth a model call.
    private const double MinimumSide = 24;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalyzePdfImagesTool"/> class.
    /// </summary>
    public AnalyzePdfImagesTool()
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
    public override string Description => "Describes the images embedded in a PDF — figures, charts, diagrams, photographs — with a vision model. For charts it lists the printed values and labels verbatim. Returns one entry per image with its number, page and position. Choose images with 'images' (numbered across the document as extract_pdf_images lists them) or 'pages', and optionally ask a 'question' about them. Describes a limited number of images per call and says which remain.";

    /// <summary>
    /// Describes the images.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One description per image.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        var question = arguments.GetString("question");
        var wanted = ReadImageNumbers(arguments);
        var limit = Math.Max(1, context.Options.MaxVisionPagesPerCall);
        var selected = new List<PdfImageEntry>();
        var skippedSmall = 0;
        var total = 0;

        using (var pdf = PdfFiles.OpenForReading(bytes, arguments.GetString("password")))
        {
            var pages = PdfPageRange.Parse(arguments.GetPages(), pdf.NumberOfPages).ToHashSet();
            var lastPage = wanted.Count > 0
                ? pdf.NumberOfPages
                : pages.Max();

            var lastImage = wanted.Count > 0
                ? wanted.Max()
                : int.MaxValue;

            // Numbers run across the whole document, so every page before the last one wanted is counted.
            for (var number = 1; number <= lastPage && total < lastImage; number++)
            {
                var page = pdf.GetPage(number);

                foreach (var image in PdfPageImages.Get(page))
                {
                    total++;

                    var box = PdfBox.From(image.BoundingBox);
                    var chosen = wanted.Count > 0
                        ? wanted.Contains(total) && (arguments.GetPages() is null || pages.Contains(number))
                        : pages.Contains(number);

                    if (!chosen)
                    {
                        continue;
                    }

                    if (wanted.Count == 0 && (box.Width < MinimumSide || box.Height < MinimumSide))
                    {
                        skippedSmall++;

                        continue;
                    }

                    selected.Add(Prepare(page, image, total, number, box, context.Options.MaxImageBytes));
                }
            }
        }

        if (selected.Count == 0)
        {
            if (wanted.Count > 0)
            {
                var why = total < wanted.Max()
                    ? $"the document has only {total} image(s)"
                    : "they are not on the selected pages";

                return $"No image numbered {string.Join(", ", wanted.Order())} can be described: {why}. Use extract_pdf_images to list the images and their numbers.";
            }

            var small = skippedSmall > 0
                ? $" ({skippedSmall} tiny decorative image(s) were skipped; name them in 'images' to describe them anyway)"
                : string.Empty;

            return $"The selected pages of {source.Describe()} have no images to describe{small}. Use extract_pdf_images to list the images and their numbers.";
        }

        var batch = selected.Take(limit).ToList();
        var remaining = selected.Skip(limit).Select(entry => entry.Number).ToList();
        var model = await PdfModelClient.ResolveVisionAsync(context.Services, cancellationToken);
        var builder = new StringBuilder();

        if (model is null)
        {
            builder.Append("No vision-capable AI model is configured on this host, so the images of ").Append(source.Describe())
                .Append(" cannot be described. They are listed below; use extract_pdf_images to show them to the user, and get_pdf_page_content for the captions printed near them.\n");

            foreach (var entry in selected)
            {
                AppendHeading(builder, entry);
            }

            return builder.ToString().TrimEnd();
        }

        var readable = batch.Where(entry => entry.Image is not null).ToList();
        var answers = await PdfModelClient.MapAsync(
            readable,
            (entry, token) => model.DescribeImageAsync(Instructions, BuildPrompt(source, entry, question), entry.Image, entry.MediaType, 1500, token),
            cancellationToken);

        for (var index = 0; index < readable.Count; index++)
        {
            readable[index].Description = answers[index];
        }

        builder.Append("Images of ").Append(source.Describe()).Append(" described by a vision model: ")
            .Append(batch.Count.ToString(CultureInfo.InvariantCulture)).Append(" of ")
            .Append(selected.Count.ToString(CultureInfo.InvariantCulture)).Append(" selected.");

        if (!string.IsNullOrWhiteSpace(question))
        {
            builder.Append(" Question: \"").Append(question).Append("\".");
        }

        builder.Append('\n');

        foreach (var entry in batch)
        {
            AppendHeading(builder, entry);

            if (entry.Image is null)
            {
                builder.Append("(Not described: ").Append(entry.Problem).Append(".)\n");
            }
            else if (string.IsNullOrWhiteSpace(entry.Description))
            {
                builder.Append("(The model returned no description.)\n");
            }
            else
            {
                builder.Append(entry.Description.Trim()).Append('\n');
            }
        }

        if (remaining.Count > 0)
        {
            builder.Append("\nNot described yet (at most ").Append(limit.ToString(CultureInfo.InvariantCulture))
                .Append(" images per call): images ").Append(PdfPageRange.Describe(remaining))
                .Append(". Call analyze_pdf_images again with 'images' set to those numbers.\n");
        }

        if (skippedSmall > 0)
        {
            builder.Append("\nSkipped ").Append(skippedSmall.ToString(CultureInfo.InvariantCulture))
                .Append(" tiny decorative image(s); name them in 'images' to describe them anyway.\n");
        }

        return builder.ToString().TrimEnd();
    }

    private static List<int> ReadImageNumbers(PdfToolArguments arguments)
    {
        var numbers = new List<int>();

        foreach (var value in arguments.GetStrings("images"))
        {
            foreach (var part in value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part.TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0)
                {
                    numbers.Add(number);
                }
            }
        }

        return [.. numbers.Distinct()];
    }

    private static PdfImageEntry Prepare(Page page, IPdfImage image, int number, int pageNumber, PdfBox box, int maxBytes)
    {
        var entry = new PdfImageEntry
        {
            Number = number,
            Page = pageNumber,
            Position = box.Describe(page),
            PixelWidth = image.WidthInSamples,
            PixelHeight = image.HeightInSamples,
            NearbyText = ReadNearbyText(page, box),
        };

        if (PdfPageImages.TryEncode(image, maxBytes, out var encoded, out var mediaType, out var problem))
        {
            entry.Image = encoded;
            entry.MediaType = mediaType;
        }
        else
        {
            entry.Problem = problem;
        }

        return entry;
    }

    private static string ReadNearbyText(Page page, PdfBox box)
    {
        try
        {
            // A caption sits just above or below its figure; text drawn inside a chart belongs to it too.
            var near = box.Inflate(54);
            var words = PdfPageText.GetWords(page)
                .Where(word => !string.IsNullOrWhiteSpace(word.Text) && near.Intersects(PdfBox.From(word.BoundingBox)))
                .Select(word => word.Text);

            var text = string.Join(' ', words);

            return text.Length > 400
                ? text[..400] + "…"
                : text;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string BuildPrompt(PdfSource source, PdfImageEntry entry, string question)
    {
        var prompt = new StringBuilder();

        prompt.Append("Image ").Append(entry.Number.ToString(CultureInfo.InvariantCulture))
            .Append(" on page ").Append(entry.Page.ToString(CultureInfo.InvariantCulture))
            .Append(" of \"").Append(source.Name).Append("\".");

        if (!string.IsNullOrWhiteSpace(entry.NearbyText))
        {
            prompt.Append("\nText printed around it on the page (may include its caption): ").Append(entry.NearbyText);
        }

        prompt.Append(string.IsNullOrWhiteSpace(question)
            ? "\nDescribe the image."
            : "\nQuestion: " + question);

        return prompt.ToString();
    }

    private static void AppendHeading(StringBuilder builder, PdfImageEntry entry)
    {
        builder.Append("\n## Image ").Append(entry.Number.ToString(CultureInfo.InvariantCulture))
            .Append(" — page ").Append(entry.Page.ToString(CultureInfo.InvariantCulture))
            .Append(" (").Append(entry.Position).Append("), ")
            .Append(entry.PixelWidth.ToString(CultureInfo.InvariantCulture)).Append('×')
            .Append(entry.PixelHeight.ToString(CultureInfo.InvariantCulture)).Append(" px\n");
    }

    /// <summary>
    /// One image an analysis call describes.
    /// </summary>
    private sealed class PdfImageEntry
    {
        /// <summary>
        /// Gets the image's number across the document.
        /// </summary>
        public int Number { get; init; }

        /// <summary>
        /// Gets the one-based page the image is on.
        /// </summary>
        public int Page { get; init; }

        /// <summary>
        /// Gets where the image is drawn, in top-left coordinates.
        /// </summary>
        public string Position { get; init; }

        /// <summary>
        /// Gets the image's width in pixels.
        /// </summary>
        public int PixelWidth { get; init; }

        /// <summary>
        /// Gets the image's height in pixels.
        /// </summary>
        public int PixelHeight { get; init; }

        /// <summary>
        /// Gets the text printed around the image.
        /// </summary>
        public string NearbyText { get; init; }

        /// <summary>
        /// Gets or sets the picture sent to the model.
        /// </summary>
        public byte[] Image { get; set; }

        /// <summary>
        /// Gets or sets the picture's media type.
        /// </summary>
        public string MediaType { get; set; }

        /// <summary>
        /// Gets or sets why the image could not be sent.
        /// </summary>
        public string Problem { get; set; }

        /// <summary>
        /// Gets or sets the model's description.
        /// </summary>
        public string Description { get; set; }
    }
}
