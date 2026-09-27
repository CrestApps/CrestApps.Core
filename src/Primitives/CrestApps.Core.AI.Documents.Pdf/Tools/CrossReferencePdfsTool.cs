using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Intelligence;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Finds what two or more PDFs have in common and where they disagree: the figures, dates and identifiers
/// they share, the names and terms they share, and labels that are given different values in different
/// documents — optionally with the model checking the most closely related passages for contradictions.
/// </summary>
internal sealed partial class CrossReferencePdfsTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.CrossReferencePdfs;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            "pdfs": {
              "type": "array",
              "items": { "type": "string" },
              "description": "The PDFs to cross-reference, two or more: working PDF names or uploaded file names. May be omitted when the conversation holds exactly two PDFs."
            },
            "topic": {
              "type": "string",
              "description": "Optional subject to focus on, for example \"payment terms\" or \"delivery dates\". Only the pages about it are compared."
            },
            "use_model": {
              "type": "boolean",
              "description": "Whether the AI model also checks the most closely related passages for contradictions. Defaults to true when a model is configured."
            },
            {{PdfToolSchemas.Password}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    private const string Instructions = """
        You compare pairs of passages taken from different documents, using only the passages supplied.
        For each pair decide one verdict:
        - CONTRADICTS: they state different facts about the same thing. Quote both conflicting values exactly.
        - CONSISTENT: they state the same fact.
        - RELATED: they concern the same subject without conflicting.
        - UNRELATED: they concern different things.
        Reply with one bullet per pair that is not UNRELATED, in the form:
        - Pair N — VERDICT: one sentence of explanation ("first.pdf" p. 2 vs "second.pdf" p. 5)
        If every pair is unrelated, reply exactly: No related statements.
        Never use outside knowledge.
        """;

    private const int MaxListed = 20;
    private const int MaxPairs = 8;
    private const int MaxPassagesPerDocument = 400;

    private static readonly string[] _valueKinds =
    [
        PdfPatternLibrary.Money,
        PdfPatternLibrary.Date,
        PdfPatternLibrary.Percentage,
        PdfPatternLibrary.Email,
        PdfPatternLibrary.Url,
        PdfPatternLibrary.Phone,
        PdfPatternLibrary.CreditCard,
        PdfPatternLibrary.Iban,
        PdfPatternLibrary.UsSocialSecurityNumber,
    ];

    private static readonly string[] _factKinds = [PdfPatternLibrary.Date, PdfPatternLibrary.Money, PdfPatternLibrary.Percentage];

    // Labels every document has with its own value; a difference there is not a conflict.
    private static readonly HashSet<string> _genericLabels = new(StringComparer.Ordinal)
    {
        "page", "date", "dated", "tel", "telephone", "phone", "fax", "email", "mail", "no", "number", "ref",
        "reference", "version", "rev", "revision", "time", "year", "figure", "fig", "table", "section", "item",
        "id", "step", "chapter", "part", "appendix", "printed", "generated", "created", "issued", "zip", "code",
        "http", "https", "www", "note", "source",
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="CrossReferencePdfsTool"/> class.
    /// </summary>
    public CrossReferencePdfsTool()
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
    public override string Description => "Cross-references two or more PDFs: lists potential conflicts (the same label given different figures or dates in different documents, such as \"Total: $5,000\" against \"Total: $5,500\"), the values they share (amounts, dates, percentages, emails, masked identifiers), the names and key terms they share, and — with the AI model — a check of the most closely related passages for contradictions. Every finding cites document and page. Pass 'topic' to focus on one subject. Use compare_pdfs instead to diff two versions of the same document.";

    /// <summary>
    /// Cross-references the PDFs.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The findings.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var names = arguments.GetStrings("pdfs");

        if (names.Count == 1)
        {
            throw new PdfToolException("Cross-referencing needs at least two PDFs; name two or more in 'pdfs'. " + context.DescribeAvailable(await context.GetStateAsync(cancellationToken)));
        }

        if (names.Count == 0)
        {
            var state = await context.GetStateAsync(cancellationToken);
            var available = PdfCorpus.ListAvailable(context, state);

            if (available.Count != 2)
            {
                throw new PdfToolException(available.Count < 2
                    ? $"Cross-referencing needs at least two PDFs, and this conversation has {available.Count}. " + context.DescribeAvailable(state)
                    : "Several PDFs are available; name the ones to cross-reference in 'pdfs'. " + context.DescribeAvailable(state));
            }
        }

        var load = await PdfCorpus.LoadManyAsync(context, names, arguments.GetString("password"), cancellationToken);

        if (load.Documents.Count < 2)
        {
            throw new PdfToolException("Fewer than two of the PDFs could be read. " + string.Join(" ", load.Skipped));
        }

        var topic = arguments.GetString("topic");
        var notes = new List<string>(load.Skipped.Select(skip => "Not read: " + skip));
        var pages = SelectPages(load.Documents, topic, notes);
        var facts = ExtractFacts(pages);
        var conflicts = FindConflicts(facts, out var agreements);
        var sharedValues = FindSharedValues(pages);
        var sharedTerms = FindSharedTerms(pages);
        var builder = new StringBuilder();

        builder.Append("Cross-reference of ").Append(load.Documents.Count.ToString(CultureInfo.InvariantCulture)).Append(" PDFs: ")
            .Append(string.Join(", ", load.Documents.Select(document => string.Create(CultureInfo.InvariantCulture, $"\"{document.Name}\" ({document.PageCount} pages)"))));

        if (!string.IsNullOrWhiteSpace(topic))
        {
            builder.Append(", focused on \"").Append(topic.Trim()).Append('"');
        }

        builder.Append(".\n");

        AppendConflicts(builder, conflicts, agreements);
        AppendList(builder, "Shared values", sharedValues, "No amount, date, percentage, email or identifier appears in more than one document.");
        AppendList(builder, "Shared names and terms", sharedTerms, "No distinctive name or term appears in more than one document.");

        if (arguments.GetBoolean("use_model") != false)
        {
            var model = await PdfModelClient.ResolveTextAsync(context.Services);

            if (model is null)
            {
                if (arguments.GetBoolean("use_model") == true)
                {
                    notes.Add("No AI model is configured on this host, so related passages were not checked for contradictions; the findings above are pattern-based.");
                }
            }
            else
            {
                builder.Append('\n').Append(await ReviewAsync(model, pages, conflicts, topic, cancellationToken)).Append('\n');
            }
        }

        foreach (var note in notes)
        {
            builder.Append("\nNote: ").Append(note);
        }

        builder.Append("\nConflicts are candidates found by matching labels; confirm each against the cited pages before reporting it.");

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Finds labels given different values in different documents.
    /// </summary>
    /// <param name="facts">The labelled values found.</param>
    /// <param name="agreements">The labels every document that states them gives the same value.</param>
    /// <returns>The conflicts, most documents first.</returns>
    internal static List<FactConflict> FindConflicts(List<Fact> facts, out List<FactConflict> agreements)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var conflicts = new List<FactConflict>();

        agreements = [];

        foreach (var group in facts.GroupBy(fact => (fact.Label, fact.Kind)))
        {
            var byDocument = group
                .GroupBy(fact => fact.Document, StringComparer.Ordinal)
                .ToDictionary(entries => entries.Key, entries => entries.ToList(), StringComparer.Ordinal);

            if (byDocument.Count < 2)
            {
                continue;
            }

            var sets = byDocument.Values
                .Select(entries => entries.Select(fact => fact.Value).ToHashSet(StringComparer.Ordinal))
                .ToList();

            var disagree = false;

            for (var first = 0; first < sets.Count && !disagree; first++)
            {
                for (var second = first + 1; second < sets.Count; second++)
                {
                    if (!sets[first].Overlaps(sets[second]))
                    {
                        disagree = true;

                        break;
                    }
                }
            }

            var entry = new FactConflict(group.Key.Label, group.Key.Kind, byDocument);

            if (disagree)
            {
                conflicts.Add(entry);
            }
            else if (sets.All(set => set.SetEquals(sets[0])))
            {
                agreements.Add(entry);
            }
        }

        return [.. conflicts
            .OrderByDescending(conflict => conflict.Documents.Count)
            .ThenBy(conflict => conflict.Label, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Reads the labelled figures and dates of pages: lines such as <c>Total: $5,000</c> and sentences such
    /// as <c>The contract value is $50,000</c>.
    /// </summary>
    /// <param name="pages">The pages.</param>
    /// <returns>The facts found.</returns>
    internal static List<Fact> ExtractFacts(IEnumerable<PdfCorpusPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var facts = new List<Fact>();
        var seen = new HashSet<(string Document, int Page, string Label, string Value)>();

        foreach (var page in pages)
        {
            foreach (var rawLine in (page.Text ?? string.Empty).Split('\n'))
            {
                var line = rawLine.Trim();

                if (line.Length < 4 || line.Length > 400)
                {
                    continue;
                }

                var colon = ColonFactPattern().Match(line);

                if (colon.Success)
                {
                    AddFact(facts, seen, page, colon.Groups["label"].Value, colon.Groups["rest"].Value, line);
                }

                foreach (Match match in VerbFactPattern().Matches(line))
                {
                    AddFact(facts, seen, page, match.Groups["label"].Value, match.Groups["rest"].Value, line);
                }
            }
        }

        return facts;
    }

    private static List<PdfCorpusPage> SelectPages(IReadOnlyList<PdfCorpusDocument> documents, string topic, List<string> notes)
    {
        var all = documents.SelectMany(document => document.Pages).Where(page => !string.IsNullOrWhiteSpace(page.Text)).ToList();

        if (string.IsNullOrWhiteSpace(topic) || all.Count == 0)
        {
            return all;
        }

        var index = new PdfBm25Index(all.Select(page => page.Text));
        var terms = PdfBm25Index.Tokenize(topic).Distinct(StringComparer.Ordinal).ToList();
        var relevant = all.Where((page, number) => index.Score(terms, number) > 0).ToList();

        if (relevant.Select(page => page.Document).Distinct(StringComparer.Ordinal).Count() < 2)
        {
            notes.Add($"Fewer than two documents mention \"{topic.Trim()}\", so every page was compared.");

            return all;
        }

        return relevant;
    }

    private static void AddFact(
        List<Fact> facts,
        HashSet<(string Document, int Page, string Label, string Value)> seen,
        PdfCorpusPage page,
        string rawLabel,
        string rest,
        string line)
    {
        var label = NormalizeLabel(rawLabel);

        // One statement can match both the "Label: value" and the "Label is value" forms; it is one fact.
        if (label is null ||
            !TryReadValue(rest, out var kind, out var value, out var display) ||
            !seen.Add((page.Document, page.Number, label, value)))
        {
            return;
        }

        facts.Add(new Fact(page.Document, page.Number, label, kind, value, display, line));
    }

    private static string NormalizeLabel(string raw)
    {
        var words = PdfBm25Index.Tokenize(raw)
            .Where(word => !word.All(char.IsDigit))
            .ToList();

        if (words.Count == 0)
        {
            return null;
        }

        // The last words name the thing; "The total contract value" and "Contract value" are one label.
        var label = string.Join(' ', words.TakeLast(2));

        return label.Length < 3 || (words.Count == 1 && _genericLabels.Contains(label))
            ? null
            : label;
    }

    private static bool TryReadValue(string rest, out string kind, out string value, out string display)
    {
        kind = null;
        value = null;
        display = null;

        var window = rest.Length > 80
            ? rest[..80]
            : rest;

        // The value must follow the label closely; a figure at the far end of the sentence belongs to something else.
        var pattern = PdfPatternLibrary.Find(window, _factKinds).FirstOrDefault(match => match.Index <= 25);
        var number = NumberPattern().Match(window);
        var useNumber = number.Success && number.Index <= 25 && (pattern is null || number.Index < pattern.Index);

        if (pattern is not null && !useNumber)
        {
            kind = pattern.Kind == PdfPatternLibrary.Money
                ? "number"
                : pattern.Kind;

            display = pattern.Value;
            value = PdfValues.Normalize(pattern.Kind, pattern.Value);

            return value is not null;
        }

        if (useNumber)
        {
            kind = "number";
            display = number.Value;
            value = PdfValues.Normalize(PdfPatternLibrary.Money, number.Value);

            return value is not null;
        }

        return false;
    }

    private static List<SharedItem> FindSharedValues(List<PdfCorpusPage> pages)
    {
        var values = new Dictionary<string, SharedItem>(StringComparer.Ordinal);

        foreach (var page in pages)
        {
            foreach (var match in PdfValues.Find(page.Text, _valueKinds))
            {
                var normalized = PdfValues.Normalize(match.Kind, match.Value);

                if (normalized is null)
                {
                    continue;
                }

                var key = match.Kind + ":" + normalized;

                if (!values.TryGetValue(key, out var item))
                {
                    item = new SharedItem($"{PdfValues.Display(match.Kind, match.Value)} ({match.Kind})");
                    values[key] = item;
                }

                item.Add(page.Document, page.Number);
            }
        }

        return Shared(values.Values);
    }

    private static List<SharedItem> FindSharedTerms(List<PdfCorpusPage> pages)
    {
        var phrases = new Dictionary<string, SharedItem>(StringComparer.Ordinal);

        foreach (var page in pages)
        {
            foreach (Match match in NamePattern().Matches(page.Text))
            {
                var phrase = LeadingArticle().Replace(PdfCorpus.CollapseWhitespace(match.Value), string.Empty);

                if (!phrase.Contains(' ', StringComparison.Ordinal))
                {
                    continue;
                }

                var key = phrase.ToLowerInvariant();

                if (!phrases.TryGetValue(key, out var item))
                {
                    item = new SharedItem(phrase);
                    phrases[key] = item;
                }

                item.Add(page.Document, page.Number);
            }
        }

        var shared = Shared(phrases.Values);
        var covered = string.Join(' ', shared.Select(item => item.Display.ToLowerInvariant()));

        // Distinctive single words: frequent in the documents that share them, but on few of all the pages.
        var pageFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        var terms = new Dictionary<string, SharedItem>(StringComparer.Ordinal);

        foreach (var page in pages)
        {
            foreach (var term in PdfBm25Index.Tokenize(page.Text).Distinct(StringComparer.Ordinal))
            {
                if (term.Length < 4 || term.Any(char.IsDigit))
                {
                    continue;
                }

                pageFrequency[term] = pageFrequency.GetValueOrDefault(term) + 1;

                if (!terms.TryGetValue(term, out var item))
                {
                    item = new SharedItem(term);
                    terms[term] = item;
                }

                item.Add(page.Document, page.Number);
            }
        }

        var distinctive = terms.Values
            .Where(item => item.Documents.Count >= 2 && !covered.Contains(item.Display, StringComparison.Ordinal))
            .Select(item => (Item: item, Score: item.Documents.Values.Min(pagesOf => pagesOf.Count) * Math.Log(1 + (pages.Count / (double)pageFrequency[item.Display]))))
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Item.Display, StringComparer.Ordinal)
            .Take(MaxListed / 2)
            .Select(entry => entry.Item);

        return [.. shared.Take(MaxListed / 2), .. distinctive];
    }

    private static List<SharedItem> Shared(IEnumerable<SharedItem> items)
    {
        return [.. items
            .Where(item => item.Documents.Count >= 2)
            .OrderByDescending(item => item.Documents.Count)
            .ThenByDescending(item => item.Documents.Values.Sum(pagesOf => pagesOf.Count))
            .ThenBy(item => item.Display, StringComparer.Ordinal)];
    }

    private static async Task<string> ReviewAsync(
        PdfModelClient model,
        List<PdfCorpusPage> pages,
        List<FactConflict> conflicts,
        string topic,
        CancellationToken cancellationToken)
    {
        var pairs = ChoosePairs(pages, conflicts, topic);

        if (pairs.Count == 0)
        {
            return "## Model review\nNo passages in different documents are close enough in wording to compare.";
        }

        var request = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(topic))
        {
            request.Append("Topic: ").Append(topic.Trim()).Append("\n\n");
        }

        for (var index = 0; index < pairs.Count; index++)
        {
            var (first, second) = pairs[index];

            request.Append("Pair ").Append((index + 1).ToString(CultureInfo.InvariantCulture)).Append(":\n")
                .Append("A: Document \"").Append(first.Document).Append("\", page ").Append(first.Page.ToString(CultureInfo.InvariantCulture)).Append(": ").Append(first.Text).Append('\n')
                .Append("B: Document \"").Append(second.Document).Append("\", page ").Append(second.Page.ToString(CultureInfo.InvariantCulture)).Append(": ").Append(second.Text).Append("\n\n");
        }

        try
        {
            var answer = await model.CompleteAsync(Instructions, request.ToString().TrimEnd(), 1500, cancellationToken);
            var review = string.IsNullOrWhiteSpace(answer)
                ? "The model returned no review."
                : answer.Trim();

            return string.Create(CultureInfo.InvariantCulture, $"## Model review of the {pairs.Count} most closely related passage pairs\n{review}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return "## Model review\nThe AI model could not be reached (" + ex.Message + "); the findings above are pattern-based.";
        }
    }

    private static List<(PdfPassage First, PdfPassage Second)> ChoosePairs(List<PdfCorpusPage> pages, List<FactConflict> conflicts, string topic)
    {
        var passages = PdfCorpus.SplitPassages(pages);
        var byDocument = passages
            .GroupBy(passage => passage.Document, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Take(MaxPassagesPerDocument).ToList(), StringComparer.Ordinal);

        var pairs = new List<(PdfPassage First, PdfPassage Second)>();
        var used = new HashSet<PdfPassage>();

        void AddPair(PdfPassage first, PdfPassage second)
        {
            // A passage may pair with several others, but the same two are compared once.
            if (pairs.Count >= MaxPairs || first is null || second is null || (used.Contains(first) && used.Contains(second)))
            {
                return;
            }

            used.Add(first);
            used.Add(second);
            pairs.Add((first, second));
        }

        // The statements behind each candidate conflict are checked first.
        foreach (var conflict in conflicts)
        {
            var sides = conflict.Documents
                .Select(entry => FindPassage(byDocument.GetValueOrDefault(entry.Key), entry.Value[0]))
                .Where(passage => passage is not null)
                .Take(2)
                .ToList();

            if (sides.Count == 2)
            {
                AddPair(sides[0], sides[1]);
            }
        }

        var names = byDocument.Keys.ToList();
        var candidates = new List<(PdfPassage First, PdfPassage Second, double Score)>();
        var topicTerms = string.IsNullOrWhiteSpace(topic)
            ? []
            : PdfBm25Index.Tokenize(topic).Distinct(StringComparer.Ordinal).ToList();

        for (var first = 0; first < names.Count; first++)
        {
            for (var second = first + 1; second < names.Count; second++)
            {
                var targets = byDocument[names[second]];
                var index = new PdfBm25Index(targets.Select(passage => passage.Text));

                foreach (var passage in byDocument[names[first]])
                {
                    var best = index.Search(passage.Text, 1);

                    if (best.Count == 0)
                    {
                        continue;
                    }

                    var score = best[0].Score;

                    if (topicTerms.Count > 0)
                    {
                        score *= 1 + index.Score(topicTerms, best[0].Index);
                    }

                    candidates.Add((passage, targets[best[0].Index], score));
                }
            }
        }

        foreach (var (first, second, _) in candidates.OrderByDescending(candidate => candidate.Score))
        {
            AddPair(first, second);
        }

        return pairs;
    }

    private static PdfPassage FindPassage(List<PdfPassage> passages, Fact fact)
    {
        return passages?
            .Where(passage => passage.Page == fact.Page)
            .FirstOrDefault(passage => passage.Text.Contains(fact.Display, StringComparison.Ordinal));
    }

    private static void AppendConflicts(StringBuilder builder, List<FactConflict> conflicts, List<FactConflict> agreements)
    {
        builder.Append("\n## Potential conflicts (").Append(conflicts.Count.ToString(CultureInfo.InvariantCulture)).Append(")\n");

        if (conflicts.Count == 0)
        {
            builder.Append("No label is given different figures or dates in different documents.\n");
        }

        foreach (var conflict in conflicts.Take(MaxListed))
        {
            builder.Append("- \"").Append(conflict.Label).Append("\": ")
                .Append(string.Join(" · ", conflict.Documents.Select(entry => DescribeFacts(entry.Key, entry.Value))))
                .Append('\n');
        }

        if (conflicts.Count > MaxListed)
        {
            builder.Append("- …and ").Append((conflicts.Count - MaxListed).ToString(CultureInfo.InvariantCulture)).Append(" more; pass 'topic' to narrow the comparison.\n");
        }

        if (agreements.Count > 0)
        {
            builder.Append("Consistent: ").Append(agreements.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" labelled figure(s) agree across documents, for example ")
                .Append(string.Join("; ", agreements.Take(5).Select(agreement => $"\"{agreement.Label}\" = {agreement.Documents.Values.First()[0].Display}")))
                .Append(".\n");
        }
    }

    private static string DescribeFacts(string document, List<Fact> facts)
    {
        var values = facts
            .GroupBy(fact => fact.Value, StringComparer.Ordinal)
            .Select(group => group.First().Display + " (" + PdfCorpus.Cite(group.Select(fact => fact.Page)) + ")");

        return $"\"{document}\" {string.Join(", ", values)}";
    }

    private static void AppendList(StringBuilder builder, string title, List<SharedItem> items, string none)
    {
        builder.Append("\n## ").Append(title).Append(" (").Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append(")\n");

        if (items.Count == 0)
        {
            builder.Append(none).Append('\n');

            return;
        }

        foreach (var item in items.Take(MaxListed))
        {
            builder.Append("- ").Append(item.Display).Append(": ")
                .Append(string.Join("; ", item.Documents.Select(entry => $"\"{entry.Key}\" {PdfCorpus.Cite(entry.Value.Take(6))}")))
                .Append('\n');
        }

        if (items.Count > MaxListed)
        {
            builder.Append("- …and ").Append((items.Count - MaxListed).ToString(CultureInfo.InvariantCulture)).Append(" more.\n");
        }
    }

    [GeneratedRegex(@"^(?<label>[A-Za-z][A-Za-z0-9 &/()'’.\-]{1,60}?)\s*[:=]\s*(?<rest>\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ColonFactPattern();

    [GeneratedRegex(@"(?<label>(?:[A-Za-z][\w&/'’\-]*[ \t]+){0,4}[A-Za-z][\w&/'’\-]*)[ \t]+(?:is|was|are|were|will be|equals|equaled|totals|totaled|totalled|amounts to|amounted to|of)\b(?=[ \t]+(?<rest>\S[^\n]*))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VerbFactPattern();

    [GeneratedRegex(@"(?<![\d.,])-?\d[\d,]*(?:\.\d+)?(?:\s?(?:million|billion|thousand|[mbk])\b)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\b[A-Z][a-zA-Z]+(?:[ \t]+(?:of|and|for|the|&|de|van|von)?[ \t]*[A-Z][a-zA-Z]+)+\b", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^(?:The|A|An|This|That|Our|Your)\s+", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingArticle();

    /// <summary>
    /// A labelled figure or date one document states.
    /// </summary>
    /// <param name="Document">The document.</param>
    /// <param name="Page">The one-based page.</param>
    /// <param name="Label">The normalized label, for example <c>contract value</c>.</param>
    /// <param name="Kind">The kind of value: <c>number</c>, <c>date</c> or <c>percentage</c>.</param>
    /// <param name="Value">The normalized value, compared across documents.</param>
    /// <param name="Display">The value as written.</param>
    /// <param name="Line">The line it was read from.</param>
    internal sealed record Fact(string Document, int Page, string Label, string Kind, string Value, string Display, string Line);

    /// <summary>
    /// A label several documents give values for.
    /// </summary>
    /// <param name="Label">The normalized label.</param>
    /// <param name="Kind">The kind of value.</param>
    /// <param name="Documents">The facts each document states for the label, by document.</param>
    internal sealed record FactConflict(string Label, string Kind, Dictionary<string, List<Fact>> Documents);

    /// <summary>
    /// A value or term, and where each document mentions it.
    /// </summary>
    private sealed class SharedItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SharedItem"/> class.
        /// </summary>
        /// <param name="display">The value or term as shown.</param>
        public SharedItem(string display)
        {
            Display = display;
        }

        /// <summary>
        /// Gets the value or term as shown.
        /// </summary>
        public string Display { get; }

        /// <summary>
        /// Gets the pages that mention it, by document.
        /// </summary>
        public Dictionary<string, SortedSet<int>> Documents { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Records a mention.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <param name="page">The one-based page.</param>
        public void Add(string document, int page)
        {
            if (!Documents.TryGetValue(document, out var pages))
            {
                pages = [];
                Documents[document] = pages;
            }

            pages.Add(page);
        }
    }
}
