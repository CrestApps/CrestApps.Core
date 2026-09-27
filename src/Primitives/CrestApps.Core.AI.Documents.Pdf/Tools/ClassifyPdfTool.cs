using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Intelligence;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Classifies a PDF by document type, from a built-in taxonomy or the caller's own categories, combining
/// layout and keyword signals with the model's judgement when a model is available.
/// </summary>
internal sealed class ClassifyPdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ClassifyPdf;

    private const string Other = "other";

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            "categories": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Optional labels to choose from, for example [\"purchase order\", \"quote\", \"other\"]. Omit to use the built-in document types: invoice, receipt, contract, agreement, resume, cover letter, letter, report, financial statement, bank statement, tax form, form, academic paper, manual, presentation, brochure, legal filing, medical record, other."
            },
            "multi_label": {
              "type": "boolean",
              "description": "Return every label that applies instead of the single best one. Defaults to false."
            }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    private const string Instructions = """
        You classify documents. Choose only from the categories given.
        - Base the decision on the supplied text and signals only.
        - Reply with JSON only: {"labels": [{"label": "<category>", "confidence": <0.0 to 1.0>}], "rationale": "<one or two sentences, citing pages such as (p. 1)>"}
        """;

    // The first pages say what a document is; the rest rarely changes the answer.
    private const int PagesRead = 10;
    private const int ModelCharacters = 16_000;
    private const double MinimumScore = 3;

    private static readonly Dictionary<string, (string Phrase, double Weight)[]> _keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["invoice"] = [("invoice", 3), ("invoice number", 3), ("invoice no", 3), ("bill to", 2), ("amount due", 2.5), ("due date", 1.5), ("subtotal", 1.5), ("payment terms", 1.5), ("remit", 1.5), ("total due", 2), ("qty", 1), ("unit price", 1.5)],
        ["receipt"] = [("receipt", 3), ("thank you for your purchase", 3), ("change due", 2.5), ("cashier", 2), ("transaction", 1), ("paid", 1), ("card ending", 1.5), ("store", 0.5)],
        ["contract"] = [("contract", 3), ("the parties", 2), ("hereinafter", 2.5), ("whereas", 2), ("governing law", 2), ("termination", 1.5), ("indemnif*", 1.5), ("in witness whereof", 2.5), ("obligations", 1), ("shall", 0.5)],
        ["agreement"] = [("agreement", 3), ("agree", 1), ("non-disclosure", 2.5), ("memorandum of understanding", 3), ("confidential information", 2), ("the parties", 1.5), ("effective date", 1.5), ("shall", 0.5)],
        ["resume"] = [("resume", 2.5), ("curriculum vitae", 3), ("work experience", 2.5), ("professional experience", 2.5), ("education", 1.5), ("skills", 1.5), ("certifications", 1), ("references available", 2), ("linkedin", 1.5)],
        ["cover letter"] = [("dear hiring manager", 3), ("cover letter", 3), ("i am writing to apply", 3), ("position", 1.5), ("my resume", 2.5), ("interview", 1.5), ("sincerely", 1), ("application", 1)],
        ["letter"] = [("dear", 2), ("sincerely", 2), ("yours truly", 2), ("kind regards", 1.5), ("best regards", 1.5), ("yours faithfully", 2), ("enclosure", 1)],
        ["report"] = [("report", 2), ("executive summary", 2.5), ("introduction", 1), ("findings", 2), ("conclusion", 1.5), ("recommendations", 2), ("methodology", 1), ("table of contents", 1), ("analysis", 1)],
        ["financial statement"] = [("balance sheet", 3), ("income statement", 3), ("statement of cash flows", 3), ("cash flow", 2), ("total assets", 2.5), ("liabilities", 2), ("shareholders' equity", 2.5), ("net income", 2), ("fiscal year", 1.5), ("revenue", 1)],
        ["bank statement"] = [("statement period", 3), ("account number", 1.5), ("opening balance", 3), ("closing balance", 3), ("beginning balance", 2.5), ("ending balance", 2.5), ("deposits", 1.5), ("withdrawals", 2), ("available balance", 2)],
        ["tax form"] = [("form 1040", 3), ("w-2", 3), ("1099", 3), ("internal revenue service", 3), ("irs", 2), ("taxpayer", 2), ("tax year", 2), ("adjusted gross income", 3), ("withholding", 2), ("vat return", 3)],
        ["form"] = [("please print", 2), ("check one", 2), ("applicant", 1.5), ("date of birth", 1.5), ("signature", 1), ("fill in", 1.5), ("office use only", 2.5), ("application form", 3)],
        ["academic paper"] = [("abstract", 2.5), ("keywords", 1.5), ("et al", 2.5), ("doi", 2), ("references", 1.5), ("methodology", 1.5), ("university", 1), ("literature review", 2.5), ("hypothesis", 1.5), ("journal", 1.5)],
        ["manual"] = [("user guide", 3), ("manual", 2.5), ("installation", 2), ("troubleshooting", 2.5), ("warning", 1), ("caution", 1.5), ("specifications", 1.5), ("step 1", 1.5), ("getting started", 2)],
        ["presentation"] = [("agenda", 2), ("thank you", 1), ("questions", 1), ("q&a", 2), ("slide", 2), ("overview", 1)],
        ["brochure"] = [("contact us", 2), ("call today", 2.5), ("discover", 1.5), ("our services", 2), ("why choose", 2.5), ("learn more", 1.5), ("visit us", 2), ("our team", 1)],
        ["legal filing"] = [("plaintiff", 3), ("defendant", 3), ("case no", 3), ("court", 2), ("motion", 2), ("attorney", 1.5), ("hereby", 1), ("petitioner", 2.5), ("respondent", 2), ("docket", 2.5)],
        ["medical record"] = [("patient", 2.5), ("diagnosis", 2.5), ("medication", 2), ("mrn", 2.5), ("physician", 2), ("allergies", 2.5), ("treatment", 1.5), ("clinical", 1.5), ("dob", 1.5), ("vital signs", 2.5)],
    };

    private static readonly string[] _defaultCategories =
    [
        "invoice",
        "receipt",
        "contract",
        "agreement",
        "resume",
        "cover letter",
        "letter",
        "report",
        "financial statement",
        "bank statement",
        "tax form",
        "form",
        "academic paper",
        "manual",
        "presentation",
        "brochure",
        "legal filing",
        "medical record",
        Other,
    ];

    private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cv"] = "resume",
        ["résumé"] = "resume",
        ["bill"] = "invoice",
        ["nda"] = "agreement",
        ["research paper"] = "academic paper",
        ["paper"] = "academic paper",
        ["slides"] = "presentation",
        ["slide deck"] = "presentation",
        ["deck"] = "presentation",
        ["user manual"] = "manual",
        ["guide"] = "manual",
        ["court filing"] = "legal filing",
        ["tax return"] = "tax form",
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ClassifyPdfTool"/> class.
    /// </summary>
    public ClassifyPdfTool()
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
    public override string Description => "Classifies a PDF by document type (invoice, receipt, contract, resume, report, bank statement, tax form, academic paper, presentation and more) or by the caller's own 'categories'. Returns the label, a confidence, a short rationale and the signals behind it (form fields, tables, page shape, keywords). Uses the AI model when one is configured, otherwise a heuristic it labels as such. Set multi_label for every label that applies.";

    /// <summary>
    /// Classifies the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The classification.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var categories = ReadCategories(arguments);
        var multiLabel = arguments.GetBoolean("multi_label") ?? false;
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        DocumentSignals signals;
        List<PdfCorpusPage> pages;

        using (var pdf = PdfFiles.OpenForReading(bytes, arguments.GetString("password")))
        {
            var numbers = Enumerable.Range(1, Math.Min(PagesRead, pdf.NumberOfPages)).ToList();

            if (pdf.NumberOfPages > PagesRead)
            {
                numbers.Add(pdf.NumberOfPages);
            }

            pages = PdfCorpus.ReadPages(pdf, source.Name, numbers, cancellationToken);
            signals = ReadSignals(pdf, numbers, pages);
        }

        var ranking = Score(categories, signals);
        var heuristic = PickLabels(ranking, multiLabel);
        var model = signals.WordCount == 0
            ? null
            : await PdfModelClient.ResolveTextAsync(context.Services);

        List<(string Label, double Confidence)> labels = null;
        string rationale = null;
        string modelNote = null;

        if (model is not null)
        {
            try
            {
                var answer = await model.CompleteAsync(
                    Instructions,
                    BuildRequest(source, categories, multiLabel, signals, ranking, pages),
                    500,
                    cancellationToken);

                (labels, rationale) = ReadAnswer(answer, categories, multiLabel);

                if (labels.Count == 0)
                {
                    modelNote = "The model's answer named no valid category, so the heuristic result is shown.";
                    labels = null;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                modelNote = "The AI model could not be reached (" + ex.Message + "), so the heuristic result is shown.";
            }
        }

        var builder = new StringBuilder();

        builder.Append("Classification of ").Append(source.Describe()).Append(" (")
            .Append(signals.PageCount.ToString(CultureInfo.InvariantCulture)).Append(" page(s))");

        if (labels is not null)
        {
            builder.Append(" by the AI model: ");
            AppendLabels(builder, labels);
            builder.Append('\n');

            if (!string.IsNullOrWhiteSpace(rationale))
            {
                builder.Append("Rationale: ").Append(rationale.Trim()).Append('\n');
            }
        }
        else
        {
            builder.Append(model is null && modelNote is null
                ? " — heuristic, because no AI model is configured on this host: "
                : " — heuristic: ");

            AppendLabels(builder, heuristic);
            builder.Append('\n');

            if (modelNote is not null)
            {
                builder.Append(modelNote).Append('\n');
            }

            builder.Append("This comes from keywords and layout signals only; if it matters, confirm it by reading the first page with extract_pdf_text.\n");
        }

        builder.Append("Signals: ").Append(DescribeSignals(signals)).Append('\n');
        builder.Append("Keyword ranking: ")
            .Append(string.Join(", ", ranking.Take(5).Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Label} {entry.Confidence:0.00}"))))
            .Append('\n');

        if (signals.WordCount == 0)
        {
            builder.Append("The pages read have no extractable text; they are probably scanned. Run ocr_pdf for a better classification.\n");
        }

        return builder.ToString().TrimEnd();
    }

    private static List<string> ReadCategories(PdfToolArguments arguments)
    {
        var requested = arguments.GetStrings("categories")
            .SelectMany(value => value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return requested.Count == 0
            ? [.. _defaultCategories]
            : requested;
    }

    private static DocumentSignals ReadSignals(PigDocument pdf, List<int> numbers, List<PdfCorpusPage> pages)
    {
        var signals = new DocumentSignals
        {
            PageCount = pdf.NumberOfPages,
            PagesRead = numbers.Count,
            Text = PdfCorpus.CollapseWhitespace(string.Join("\n", pages.Select(page => page.Text))).ToLowerInvariant(),
        };

        try
        {
            if (pdf.TryGetForm(out var form))
            {
                signals.FormFields = form.Fields.Count;
            }
        }
        catch (Exception)
        {
            // A damaged form dictionary only means the signal is missing.
        }

        foreach (var number in numbers)
        {
            try
            {
                var page = pdf.GetPage(number);

                if (page.Width > page.Height * 1.1)
                {
                    signals.LandscapePages++;
                }

                signals.Images += PdfPageImages.Get(page).Count;
            }
            catch (Exception)
            {
                // A page that cannot be parsed adds nothing.
            }
        }

        foreach (var page in pages)
        {
            foreach (var line in page.Text.Split('\n'))
            {
                // A line with three or more figures on it is a table row far more often than prose.
                if (PdfPatternLibrary.Find(line, [PdfPatternLibrary.Money, PdfPatternLibrary.Percentage]).Count >= 2 ||
                    line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(IsNumber) >= 3)
                {
                    signals.TableLines++;
                }
            }
        }

        signals.WordCount = signals.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        return signals;
    }

    private static List<(string Label, double Confidence)> Score(List<string> categories, DocumentSignals signals)
    {
        var scores = new List<(string Label, double Score)>();
        var matches = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories)
        {
            var known = Canonical(category);
            var keywords = known is not null && _keywords.TryGetValue(known, out var builtIn)
                ? builtIn
                : OwnKeywords(category);

            var score = 0d;

            foreach (var (phrase, weight) in keywords)
            {
                var count = CountPhrase(signals.Text, phrase);

                if (count > 0)
                {
                    score += weight * Math.Min(count, 4);
                    signals.Keywords.Add(string.Create(CultureInfo.InvariantCulture, $"\"{phrase.TrimEnd('*')}\" ×{count}"));
                }
            }

            score += Boost(known, signals);
            scores.Add((category, score));
        }

        var total = scores.Sum(entry => Math.Max(entry.Score, 0)) + 1;
        var ranking = scores
            .Select(entry => (entry.Label, Confidence: Math.Round(Math.Min(0.95, Math.Max(entry.Score, 0) / total), 2)))
            .OrderByDescending(entry => entry.Confidence)
            .ThenBy(entry => categories.IndexOf(entry.Label))
            .ToList();

        // A document no category speaks for is "other", when the caller offered it.
        var best = scores.Count == 0
            ? 0
            : scores.Max(entry => entry.Score);

        var other = categories.FirstOrDefault(category => string.Equals(category, Other, StringComparison.OrdinalIgnoreCase));

        if (best < MinimumScore && other is not null)
        {
            ranking.RemoveAll(entry => entry.Label == other);
            ranking.Insert(0, (other, 0.3));
        }

        return ranking;
    }

    private static double Boost(string category, DocumentSignals signals)
    {
        var hasTables = signals.TableLines >= 3;
        var hasForm = signals.FormFields > 0;
        var wordsPerPage = signals.WordCount / (double)Math.Max(signals.PagesRead, 1);
        var mostlyLandscape = signals.PagesRead > 0 && signals.LandscapePages >= signals.PagesRead * 0.6;
        var isLong = signals.PageCount >= 5;

        return category switch
        {
            "form" when hasForm => 4 + (Math.Min(signals.FormFields, 20) * 0.3),
            "tax form" when hasForm => 2,
            "presentation" when mostlyLandscape && wordsPerPage < 150 => 6,
            "invoice" or "receipt" or "bank statement" when signals.PageCount > 8 => -3,
            "invoice" or "receipt" or "bank statement" when hasTables => 1.5,
            "financial statement" when hasTables => 2,
            "resume" or "cover letter" or "letter" when signals.PageCount > 4 => -3,
            "academic paper" or "manual" or "report" when isLong => 1,
            _ => 0,
        };
    }

    private static List<(string Label, double Confidence)> PickLabels(List<(string Label, double Confidence)> ranking, bool multiLabel)
    {
        if (ranking.Count == 0)
        {
            return [];
        }

        if (!multiLabel)
        {
            return [ranking[0]];
        }

        var threshold = Math.Max(0.1, ranking[0].Confidence * 0.5);

        return [.. ranking.Where(entry => entry.Confidence >= threshold).Take(5)];
    }

    private static string BuildRequest(
        PdfSource source,
        List<string> categories,
        bool multiLabel,
        DocumentSignals signals,
        List<(string Label, double Confidence)> ranking,
        List<PdfCorpusPage> pages)
    {
        var builder = new StringBuilder();

        builder.Append("Categories: ").Append(string.Join(", ", categories)).Append('\n');
        builder.Append(multiLabel
            ? "Give every category that clearly applies, best first.\n"
            : "Give exactly one label: the best category.\n");

        builder.Append("Signals: ").Append(DescribeSignals(signals)).Append('\n');
        builder.Append("Keyword heuristic ranking (a hint only): ")
            .Append(string.Join(", ", ranking.Take(5).Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Label} {entry.Confidence:0.00}"))))
            .Append("\n\n");

        var text = PdfCorpus.Format(pages);

        if (text.Length > ModelCharacters)
        {
            text = text[..ModelCharacters] + "\n[…text cut…]";
        }

        builder.Append("Text of \"").Append(source.Name).Append("\" (its first pages):\n").Append(text);

        return builder.ToString();
    }

    private static (List<(string Label, double Confidence)> Labels, string Rationale) ReadAnswer(string answer, List<string> categories, bool multiLabel)
    {
        var labels = new List<(string Label, double Confidence)>();
        var json = PdfModelJson.ParseObject(answer);

        if (json is null)
        {
            return (labels, null);
        }

        var items = json["labels"] as JsonArray ?? json["items"] as JsonArray;

        if (items is null && json["label"] is { } single)
        {
            items = [new JsonObject { ["label"] = single.DeepClone(), ["confidence"] = json["confidence"]?.DeepClone() }];
        }

        foreach (var item in items ?? [])
        {
            var name = item is JsonObject entry
                ? PdfModelJson.GetText(entry["label"] ?? entry["category"] ?? entry["name"])
                : PdfModelJson.GetText(item);

            var category = Match(name, categories);

            if (category is null || labels.Exists(label => label.Label == category))
            {
                continue;
            }

            var confidence = item is JsonObject withConfidence
                ? PdfModelJson.GetNumber(withConfidence["confidence"]) ?? 0.5
                : 0.5;

            // A model sometimes answers in percent.
            if (confidence > 1)
            {
                confidence /= 100;
            }

            labels.Add((category, Math.Round(Math.Clamp(confidence, 0, 1), 2)));
        }

        if (!multiLabel && labels.Count > 1)
        {
            labels = [labels[0]];
        }

        return (labels, PdfModelJson.GetText(json["rationale"] ?? json["reason"]));
    }

    private static string Match(string name, List<string> categories)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim().Trim('"', '.');

        return categories.FirstOrDefault(category => string.Equals(category, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? categories.FirstOrDefault(category => string.Equals(Canonical(category), Canonical(trimmed), StringComparison.OrdinalIgnoreCase) && Canonical(trimmed) is not null)
            ?? categories.FirstOrDefault(category => trimmed.Contains(category, StringComparison.OrdinalIgnoreCase));
    }

    private static string Canonical(string category)
    {
        var trimmed = category?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (_keywords.ContainsKey(trimmed))
        {
            return trimmed.ToLowerInvariant();
        }

        return _aliases.GetValueOrDefault(trimmed);
    }

    private static (string Phrase, double Weight)[] OwnKeywords(string category)
    {
        // A category the taxonomy does not know is looked for by its own words.
        var words = PdfBm25Index.Tokenize(category).Distinct(StringComparer.Ordinal).Select(word => (word, 1.5));

        return [(category.Trim().ToLowerInvariant(), 3d), .. words];
    }

    private static void AppendLabels(StringBuilder builder, List<(string Label, double Confidence)> labels)
    {
        builder.Append(string.Join(", ", labels.Select(label => string.Create(CultureInfo.InvariantCulture, $"{label.Label} (confidence {label.Confidence:0.00})"))));
    }

    private static string DescribeSignals(DocumentSignals signals)
    {
        var parts = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"{signals.PageCount} page(s), {signals.PagesRead} read"),
            string.Create(CultureInfo.InvariantCulture, $"{signals.FormFields} form field(s)"),
            signals.TableLines >= 3
                ? string.Create(CultureInfo.InvariantCulture, $"tables likely ({signals.TableLines} rows of figures)")
                : "no tables detected",
            string.Create(CultureInfo.InvariantCulture, $"{signals.Images} image(s)"),
            string.Create(CultureInfo.InvariantCulture, $"{signals.LandscapePages} landscape page(s)"),
            string.Create(CultureInfo.InvariantCulture, $"{signals.WordCount} words"),
        };

        if (signals.Keywords.Count > 0)
        {
            parts.Add("keywords " + string.Join(", ", signals.Keywords.Distinct().Take(8)));
        }

        return string.Join("; ", parts);
    }

    private static int CountPhrase(string text, string phrase)
    {
        // A phrase ending in '*' is a stem: "indemnif*" counts indemnify and indemnification.
        var stem = phrase?.EndsWith('*') == true;

        phrase = phrase?.TrimEnd('*');

        if (string.IsNullOrEmpty(phrase))
        {
            return 0;
        }

        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(phrase, index, StringComparison.Ordinal)) >= 0)
        {
            var end = index + phrase.Length;
            var startsWord = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var endsWord = stem || end >= text.Length || !char.IsLetterOrDigit(text[end]);

            if (startsWord && endsWord)
            {
                count++;
            }

            index = end;
        }

        return count;
    }

    private static bool IsNumber(string token)
    {
        var trimmed = token.Trim('$', '€', '£', '%', '(', ')', ',', '.');

        return trimmed.Length > 0 &&
            trimmed.Any(char.IsDigit) &&
            trimmed.All(character => char.IsDigit(character) || character is ',' or '.' or '-');
    }

    /// <summary>
    /// What the classifier measured about a document.
    /// </summary>
    private sealed class DocumentSignals
    {
        /// <summary>
        /// Gets the number of pages the document has.
        /// </summary>
        public int PageCount { get; init; }

        /// <summary>
        /// Gets the number of pages read.
        /// </summary>
        public int PagesRead { get; init; }

        /// <summary>
        /// Gets the lower-cased text of the pages read.
        /// </summary>
        public string Text { get; init; }

        /// <summary>
        /// Gets or sets the number of form fields.
        /// </summary>
        public int FormFields { get; set; }

        /// <summary>
        /// Gets or sets the number of lines that read as table rows.
        /// </summary>
        public int TableLines { get; set; }

        /// <summary>
        /// Gets or sets the number of images on the pages read.
        /// </summary>
        public int Images { get; set; }

        /// <summary>
        /// Gets or sets the number of landscape pages among those read.
        /// </summary>
        public int LandscapePages { get; set; }

        /// <summary>
        /// Gets or sets the number of words read.
        /// </summary>
        public int WordCount { get; set; }

        /// <summary>
        /// Gets the keywords found, with their counts.
        /// </summary>
        public List<string> Keywords { get; } = [];
    }
}
