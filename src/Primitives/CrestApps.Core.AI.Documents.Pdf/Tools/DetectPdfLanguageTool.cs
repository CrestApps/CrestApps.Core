using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Tells what language a PDF is written in, overall and page by page, and whether that matches the
/// language the document declares for screen readers and search.
/// </summary>
internal sealed class DetectPdfLanguageTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.DetectPdfLanguage;

    private const int MaxCharactersPerPage = 20_000;
    private const int MaxSampleCharacters = 80_000;
    private const int MinLettersPerPage = 40;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "per_page": {
              "type": "boolean",
              "description": "Also report the language of each page. Defaults to false; a document that mixes languages is reported either way."
            }
          },
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="DetectPdfLanguageTool"/> class.
    /// </summary>
    public DetectPdfLanguageTool()
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
    public override string Description => "Detects the language a PDF is written in (as a BCP 47 code such as 'en' or 'fr', with its name, script and a confidence), optionally page by page, and compares it with the language the document declares in its catalog. Works offline from the text's script and common words; scanned pages without text need ocr_pdf first.";

    /// <summary>
    /// Detects the language.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The conversation's PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The detected and declared languages.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var perPage = arguments.GetBoolean("per_page") ?? false;

        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        using var pdf = PdfFiles.OpenForReading(bytes, arguments.GetString("password"));
        var pages = PdfPageRange.Parse(arguments.GetPages(), pdf.NumberOfPages);

        var detections = new List<(int Page, PdfLanguageDetection Detection)>();
        var texts = new List<string>();

        foreach (var number in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = PdfPageText.GetPlainText(pdf.GetPage(number));

            if (text.Length > MaxCharactersPerPage)
            {
                text = text[..MaxCharactersPerPage];
            }

            texts.Add(text);
            detections.Add((number, PdfLanguageDetector.Detect(text)));
        }

        var overall = PdfLanguageDetector.Detect(Sample(texts));
        var declared = PdfLanguageDetector.ReadDeclaredLanguage(pdf);
        var scope = PdfPageSelection.Describe(pages, pdf.NumberOfPages);
        var writer = new PdfResponseWriter(context.Options.MaxToolResponseCharacters);

        if (overall.Code == "und")
        {
            writer.Line($"The language of \"{source.Name}\" ({scope}) could not be told: {DescribeTooLittle(overall)}.");
        }
        else
        {
            writer.Line(FormattableString.Invariant($"\"{source.Name}\" ({scope}) is written in {overall.Name} ({overall.Code}), {overall.Script} script, confidence {overall.Confidence:0.00}, from {overall.Letters:N0} letters."));

            if (overall.Alternative is not null)
            {
                writer.Line($"{PdfLanguageDetector.NameOf(overall.Alternative)} ({overall.Alternative}) is close behind; the two are hard to tell apart from common words alone.");
            }
        }

        writer.Line(DescribeDeclared(declared, overall));

        var groups = Group(detections);
        var languages = groups.Where(group => group.Code != "und").Select(group => group.Code).Distinct(StringComparer.Ordinal).ToList();

        if (languages.Count > 1)
        {
            writer.Line("The document mixes languages: " + string.Join("; ", groups.Where(group => group.Code != "und").Select(DescribeGroup)) + ".");
        }

        if (perPage)
        {
            writer.Line();
            writer.Line("By page:");

            foreach (var (page, detection) in detections)
            {
                var line = detection.Code == "und"
                    ? FormattableString.Invariant($"p{page}: undetermined ({DescribeTooLittle(detection)})")
                    : FormattableString.Invariant($"p{page}: {detection.Name} ({detection.Code}), confidence {detection.Confidence:0.00}");

                if (!writer.TryLine(line))
                {
                    writer.Line("[Not every page is listed, to stay within the answer size; narrow 'pages' to see the rest.]");

                    break;
                }
            }
        }

        return writer.ToString();
    }

    private static string Sample(List<string> texts)
    {
        var total = texts.Sum(text => text.Length);

        if (total <= MaxSampleCharacters)
        {
            return string.Join('\n', texts);
        }

        // A long document is sampled evenly, so a language that starts half way through still counts.
        var share = (double)MaxSampleCharacters / total;
        var builder = new StringBuilder(MaxSampleCharacters + texts.Count);

        foreach (var text in texts)
        {
            var take = (int)Math.Ceiling(text.Length * share);

            builder.Append(text, 0, Math.Min(text.Length, take)).Append('\n');
        }

        return builder.ToString();
    }

    private static string DescribeDeclared(string declared, PdfLanguageDetection overall)
    {
        if (declared is null)
        {
            return "The document declares no language (catalog /Lang). Screen readers and search then guess; edit_pdf_metadata can set it.";
        }

        var text = $"Declared language (catalog /Lang): {declared} ({PdfLanguageDetector.NameOf(declared)})";

        if (overall.Code == "und")
        {
            return text + ".";
        }

        return string.Equals(PdfLanguageDetector.PrimaryTag(declared), overall.Code, StringComparison.Ordinal)
            ? text + " — matches the text."
            : text + $" — does NOT match the text, which reads as {overall.Name} ({overall.Code}). edit_pdf_metadata can correct it.";
    }

    private static string DescribeTooLittle(PdfLanguageDetection detection)
    {
        return detection.Letters < MinLettersPerPage
            ? detection.Letters.ToString(CultureInfo.InvariantCulture) + " letters of text is too little (a scanned page needs ocr_pdf first)"
            : "the words are not ones this detector recognises";
    }

    private static List<(string Code, string Name, List<int> Pages)> Group(List<(int Page, PdfLanguageDetection Detection)> detections)
    {
        var groups = new List<(string Code, string Name, List<int> Pages)>();

        foreach (var (page, detection) in detections)
        {
            // A page with little text says little; it is not taken as a change of language.
            if (detection.Letters < MinLettersPerPage || detection.Confidence < 0.3)
            {
                continue;
            }

            if (groups.Count > 0 && groups[^1].Code == detection.Code)
            {
                groups[^1].Pages.Add(page);

                continue;
            }

            groups.Add((detection.Code, detection.Name, [page]));
        }

        return groups;
    }

    private static string DescribeGroup((string Code, string Name, List<int> Pages) group)
    {
        var pages = group.Pages.Count == 1
            ? "page " + group.Pages[0].ToString(CultureInfo.InvariantCulture)
            : "pages " + PdfPageRange.Describe(group.Pages);

        return $"{pages}: {group.Name} ({group.Code})";
    }
}
