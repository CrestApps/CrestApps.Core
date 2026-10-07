using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using UglyToad.PdfPig;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Compares PDFs with a baseline and reports what text was added, removed and changed, with the pages on
/// both sides, along with differences in page count, page size and document properties.
/// </summary>
/// <remarks>
/// The documents are compared as a reader reads them — lines of text in reading order — so a change of
/// layout that leaves the words alone is not reported, and text that moved to another page is matched
/// where it now is. Lines holding only a page number are left out, since every inserted page renumbers the
/// rest.
/// </remarks>
internal sealed class ComparePdfsTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ComparePdfs;

    private const int DefaultMaxChanges = 60;
    private const int MaxMaxChanges = 300;
    private const int MaxPages = 1_000;
    private const int MaxLines = 30_000;
    private const int MaxLinesShownPerChange = 12;
    private const int MaxChangeCharacters = 1_500;
    private const double PairingSimilarity = 0.5;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            "pdfs": {
              "type": "array",
              "items": { "type": "string" },
              "description": "The PDFs to compare, by name (working PDF names, or uploaded file names or document ids). The first is the baseline; each of the others is compared with it."
            },
            "pdf": {
              "type": "string",
              "description": "The baseline PDF, when 'pdfs' is not given."
            },
            "other": {
              "type": "string",
              "description": "The PDF compared with 'pdf', when 'pdfs' is not given."
            },
            {{PdfToolSchemas.Password}},
            "granularity": {
              "type": "string",
              "enum": ["line", "word"],
              "description": "'line' (default) lists removed and added lines; 'word' shows each change word by word, as [-removed-]{+added+}."
            },
            "max_changes": {
              "type": "integer",
              "description": "The most changes listed per comparison, 1-300. Defaults to 60; every change is still counted."
            }
          },
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComparePdfsTool"/> class.
    /// </summary>
    public ComparePdfsTool()
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
    public override string Description => "Compares two or more PDFs with a baseline (the first one) and reports the text added, removed and changed, each change with its page in both documents, plus differences in page count, page sizes and title/author/subject/keywords. Use granularity 'word' to see exactly which words changed. Compares text in reading order, so pure layout changes are not reported.";

    /// <summary>
    /// Compares the PDFs.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The conversation's PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The differences.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var granularity = (arguments.GetString("granularity") ?? "line").Trim().ToLowerInvariant();

        if (granularity is not ("line" or "word"))
        {
            throw new PdfToolException($"\"{granularity}\" is not a comparison granularity. Use 'line' or 'word'.");
        }

        var maxChanges = Math.Clamp(arguments.GetInt("max_changes") ?? DefaultMaxChanges, 1, MaxMaxChanges);
        var names = ReadNames(arguments);
        var password = arguments.GetString("password");
        var documents = new List<ComparedDocument>(names.Count);

        foreach (var name in names)
        {
            var source = await context.FindPdfAsync(name, cancellationToken);
            var bytes = await context.ReadPdfAsync(source, cancellationToken);
            using var pdf = PdfFiles.OpenForReading(bytes, password);

            documents.Add(Read(source.Name, pdf, cancellationToken));
        }

        var writer = new PdfResponseWriter(context.Options.MaxToolResponseCharacters);
        var baseline = documents[0];

        for (var index = 1; index < documents.Count; index++)
        {
            if (index > 1)
            {
                writer.Line();
            }

            Compare(writer, baseline, documents[index], granularity, maxChanges);
        }

        writer.Line();
        writer.Line("Text is compared line by line in reading order; lines that hold only a page number are ignored.");

        if (documents.Exists(document => document.Truncated))
        {
            writer.Line(FormattableString.Invariant($"Only the first {MaxPages} pages or {MaxLines:N0} lines of a document are compared."));
        }

        return writer.ToString();
    }

    private static List<string> ReadNames(PdfToolArguments arguments)
    {
        var names = arguments.GetStrings("pdfs");

        // A list written as one comma-separated string.
        if (names.Count == 1 && names[0].Contains(',', StringComparison.Ordinal))
        {
            names = [.. names[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        }

        if (names.Count == 0)
        {
            var first = arguments.Pdf();
            var other = arguments.GetString("other");

            if (first is not null)
            {
                names.Add(first);
            }

            if (other is not null)
            {
                names.Add(other);
            }
        }

        if (names.Count < 2)
        {
            throw new PdfToolException("Name at least two PDFs to compare: 'pdfs' as a list (the first is the baseline), or 'pdf' and 'other'.");
        }

        if (names.Count > 6)
        {
            throw new PdfToolException("Compare at most 6 PDFs at a time.");
        }

        return names;
    }

    private static ComparedDocument Read(string name, PdfDocument pdf, CancellationToken cancellationToken)
    {
        var document = new ComparedDocument
        {
            Name = name,
            PageCount = pdf.NumberOfPages,
            Title = pdf.Information?.Title,
            Author = pdf.Information?.Author,
            Subject = pdf.Information?.Subject,
            Keywords = pdf.Information?.Keywords,
        };

        for (var number = 1; number <= pdf.NumberOfPages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (number > MaxPages || document.Lines.Count >= MaxLines)
            {
                document.Truncated = true;

                break;
            }

            var page = pdf.GetPage(number);

            document.PageSizes.Add(PdfPageSizes.Describe(page.Width, page.Height));

            foreach (var block in PdfPageText.GetBlocks(page))
            {
                foreach (var line in block.TextLines)
                {
                    var text = PdfTextPatterns.OneLine(line.Text);

                    if (text.Length == 0 || PdfTextPatterns.IsPageNumber(text))
                    {
                        continue;
                    }

                    document.Lines.Add(text);
                    document.LinePages.Add(number);
                }
            }
        }

        return document;
    }

    private static void Compare(PdfResponseWriter writer, ComparedDocument baseline, ComparedDocument other, string granularity, int maxChanges)
    {
        var edits = PdfTextDiff.Diff(baseline.Lines, other.Lines);
        var hunks = BuildHunks(edits);
        var added = 0;
        var removed = 0;
        var changed = 0;

        foreach (var hunk in hunks)
        {
            hunk.Operations = Pair(baseline, other, hunk);

            foreach (var operation in hunk.Operations)
            {
                switch (operation.Kind)
                {
                    case '+':
                        added++;

                        break;
                    case '-':
                        removed++;

                        break;
                    default:
                        changed++;

                        break;
                }
            }
        }

        writer.Line($"Compared \"{other.Name}\" with the baseline \"{baseline.Name}\":");
        writer.Line(FormattableString.Invariant($"- Pages: {baseline.PageCount} vs {other.PageCount}. Lines of text: {baseline.Lines.Count:N0} vs {other.Lines.Count:N0}."));

        if (hunks.Count == 0)
        {
            writer.Line("- The text is the same.");
        }
        else
        {
            writer.Line(FormattableString.Invariant($"- {hunks.Count} change(s): {changed} line(s) changed, {added} added, {removed} removed."));
        }

        foreach (var difference in DescribeProperties(baseline, other))
        {
            writer.Line("- " + difference);
        }

        foreach (var difference in DescribePageSizes(baseline, other))
        {
            writer.Line("- " + difference);
        }

        if (hunks.Count == 0)
        {
            return;
        }

        writer.Line();
        writer.Line($"Changes (baseline page ↔ other page){(granularity == "word" ? "; [-removed-] {+added+}" : "; - removed, + added")}:");

        var shown = 0;

        foreach (var hunk in hunks)
        {
            if (shown >= maxChanges)
            {
                break;
            }

            var lines = granularity == "word"
                ? DescribeWords(baseline, other, hunk, shown + 1)
                : DescribeLines(baseline, other, hunk, shown + 1);

            if (!writer.Fits(lines.Sum(line => line.Length + 1)))
            {
                break;
            }

            foreach (var line in lines)
            {
                writer.TryLine(line);
            }

            shown++;
        }

        if (shown < hunks.Count)
        {
            writer.Line(FormattableString.Invariant($"[Listed {shown} of {hunks.Count} changes. Raise max_changes (up to {MaxMaxChanges}) or compare fewer documents at once to see more.]"));
        }
    }

    private static List<Hunk> BuildHunks(List<PdfDiffEdit> edits)
    {
        var hunks = new List<Hunk>();
        Hunk current = null;

        foreach (var edit in edits)
        {
            if (edit.Kind == PdfDiffKind.Equal)
            {
                current = null;

                continue;
            }

            if (current is null)
            {
                current = new Hunk
                {
                    BaselineStart = edit.BaselineStart,
                    BaselineEnd = edit.BaselineStart,
                    OtherStart = edit.OtherStart,
                    OtherEnd = edit.OtherStart,
                };

                hunks.Add(current);
            }

            if (edit.Kind == PdfDiffKind.Delete)
            {
                current.BaselineEnd = edit.BaselineStart + edit.Length;
            }
            else
            {
                current.OtherEnd = edit.OtherStart + edit.Length;
            }
        }

        return hunks;
    }

    private static List<Operation> Pair(ComparedDocument baseline, ComparedDocument other, Hunk hunk)
    {
        var operations = new List<Operation>();
        var i = hunk.BaselineStart;
        var j = hunk.OtherStart;

        while (i < hunk.BaselineEnd && j < hunk.OtherEnd)
        {
            if (PdfTextDiff.Similarity(baseline.Lines[i], other.Lines[j]) >= PairingSimilarity)
            {
                operations.Add(new Operation('~', i++, j++));

                continue;
            }

            var ahead = LookAhead(other.Lines, j, hunk.OtherEnd, baseline.Lines[i]);

            if (ahead > 0)
            {
                for (var step = 0; step < ahead; step++)
                {
                    operations.Add(new Operation('+', -1, j++));
                }

                continue;
            }

            var behind = LookAhead(baseline.Lines, i, hunk.BaselineEnd, other.Lines[j]);

            if (behind > 0)
            {
                for (var step = 0; step < behind; step++)
                {
                    operations.Add(new Operation('-', i++, -1));
                }

                continue;
            }

            operations.Add(new Operation('-', i++, -1));
            operations.Add(new Operation('+', -1, j++));
        }

        while (i < hunk.BaselineEnd)
        {
            operations.Add(new Operation('-', i++, -1));
        }

        while (j < hunk.OtherEnd)
        {
            operations.Add(new Operation('+', -1, j++));
        }

        return operations;
    }

    private static int LookAhead(List<string> lines, int start, int end, string match)
    {
        for (var step = 1; step <= 3 && start + step < end; step++)
        {
            if (PdfTextDiff.Similarity(lines[start + step], match) >= PairingSimilarity)
            {
                return step;
            }
        }

        return 0;
    }

    private static List<string> DescribeLines(ComparedDocument baseline, ComparedDocument other, Hunk hunk, int number)
    {
        var lines = new List<string> { Heading(baseline, other, hunk, number) };
        var shown = 0;

        foreach (var operation in hunk.Operations)
        {
            if (shown >= MaxLinesShownPerChange)
            {
                lines.Add(FormattableString.Invariant($"   … {hunk.Operations.Count - shown} more line(s) in this change"));

                break;
            }

            switch (operation.Kind)
            {
                case '-':
                    lines.Add("   - " + Shorten(baseline.Lines[operation.Baseline]));

                    break;
                case '+':
                    lines.Add("   + " + Shorten(other.Lines[operation.Other]));

                    break;
                default:
                    lines.Add("   - " + Shorten(baseline.Lines[operation.Baseline]));
                    lines.Add("   + " + Shorten(other.Lines[operation.Other]));

                    break;
            }

            shown++;
        }

        return lines;
    }

    private static List<string> DescribeWords(ComparedDocument baseline, ComparedDocument other, Hunk hunk, int number)
    {
        // The unchanged line on either side of a change is included, so the change reads in its context
        // and a paragraph that merely rewrapped shows only the words that differ.
        var before = baseline.Lines.Skip(hunk.BaselineStart).Take(hunk.BaselineEnd - hunk.BaselineStart).ToList();
        var after = other.Lines.Skip(hunk.OtherStart).Take(hunk.OtherEnd - hunk.OtherStart).ToList();
        if (hunk.BaselineStart > 0)
        {
            var leading = baseline.Lines[hunk.BaselineStart - 1];

            before.Insert(0, leading);
            after.Insert(0, leading);
        }

        if (hunk.BaselineEnd < baseline.Lines.Count)
        {
            var trailing = baseline.Lines[hunk.BaselineEnd];

            before.Add(trailing);
            after.Add(trailing);
        }

        var diff = PdfTextDiff.WordDiff(string.Join(' ', before), string.Join(' ', after));

        return [Heading(baseline, other, hunk, number), "   " + PdfTextPatterns.Truncate(diff, MaxChangeCharacters)];
    }

    private static string Heading(ComparedDocument baseline, ComparedDocument other, Hunk hunk, int number)
    {
        var kind = "changed";

        if (hunk.BaselineEnd == hunk.BaselineStart)
        {
            kind = "added";
        }
        else if (hunk.OtherEnd == hunk.OtherStart)
        {
            kind = "removed";
        }

        return FormattableString.Invariant($"{number}. {kind} — p.{PagesOf(baseline, hunk.BaselineStart, hunk.BaselineEnd)} ↔ p.{PagesOf(other, hunk.OtherStart, hunk.OtherEnd)}");
    }

    private static string PagesOf(ComparedDocument document, int start, int end)
    {
        if (document.LinePages.Count == 0)
        {
            return "1";
        }

        if (end > start)
        {
            return PdfPageRange.Describe(document.LinePages.Skip(start).Take(end - start));
        }

        // Nothing on this side: the change sits where the neighbouring text is.
        var index = Math.Clamp(start - 1, 0, document.LinePages.Count - 1);

        return document.LinePages[index].ToString(CultureInfo.InvariantCulture);
    }

    private static string Shorten(string line)
    {
        return PdfTextPatterns.Truncate(line, 300);
    }

    private static List<string> DescribeProperties(ComparedDocument baseline, ComparedDocument other)
    {
        var differences = new List<string>();

        Add(differences, "title", baseline.Title, other.Title);
        Add(differences, "author", baseline.Author, other.Author);
        Add(differences, "subject", baseline.Subject, other.Subject);
        Add(differences, "keywords", baseline.Keywords, other.Keywords);

        return differences;

        static void Add(List<string> list, string name, string first, string second)
        {
            first = first?.Trim() ?? string.Empty;
            second = second?.Trim() ?? string.Empty;

            if (!string.Equals(first, second, StringComparison.Ordinal))
            {
                list.Add($"The {name} differs: \"{(first.Length == 0 ? "(none)" : first)}\" → \"{(second.Length == 0 ? "(none)" : second)}\".");
            }
        }
    }

    private static List<string> DescribePageSizes(ComparedDocument baseline, ComparedDocument other)
    {
        var differences = new List<string>();
        var shared = Math.Min(baseline.PageSizes.Count, other.PageSizes.Count);
        var mismatched = new List<int>();

        for (var index = 0; index < shared; index++)
        {
            if (!string.Equals(baseline.PageSizes[index], other.PageSizes[index], StringComparison.Ordinal))
            {
                mismatched.Add(index + 1);
            }
        }

        if (mismatched.Count > 0)
        {
            var examples = mismatched.Take(5).Select(page => FormattableString.Invariant($"page {page}: {baseline.PageSizes[page - 1]} vs {other.PageSizes[page - 1]}"));

            differences.Add(FormattableString.Invariant($"Page size differs on {mismatched.Count} page(s): ") + string.Join("; ", examples) + (mismatched.Count > 5 ? "; …" : string.Empty) + ".");
        }

        return differences;
    }

    /// <summary>
    /// A document's text, lines in reading order with the page each is on.
    /// </summary>
    private sealed class ComparedDocument
    {
        /// <summary>
        /// Gets or sets the name the document is referred to by.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the number of pages.
        /// </summary>
        public int PageCount { get; set; }

        /// <summary>
        /// Gets or sets the title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the author.
        /// </summary>
        public string Author { get; set; }

        /// <summary>
        /// Gets or sets the subject.
        /// </summary>
        public string Subject { get; set; }

        /// <summary>
        /// Gets or sets the keywords.
        /// </summary>
        public string Keywords { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether only part of the document was read.
        /// </summary>
        public bool Truncated { get; set; }

        /// <summary>
        /// Gets the lines of text.
        /// </summary>
        public List<string> Lines { get; } = [];

        /// <summary>
        /// Gets the page of each line.
        /// </summary>
        public List<int> LinePages { get; } = [];

        /// <summary>
        /// Gets the paper size of each page.
        /// </summary>
        public List<string> PageSizes { get; } = [];
    }

    /// <summary>
    /// A run of lines that differ between the two documents, between two runs they share.
    /// </summary>
    private sealed class Hunk
    {
        /// <summary>
        /// Gets or sets where the run starts in the baseline.
        /// </summary>
        public int BaselineStart { get; set; }

        /// <summary>
        /// Gets or sets where the run ends in the baseline, exclusive.
        /// </summary>
        public int BaselineEnd { get; set; }

        /// <summary>
        /// Gets or sets where the run starts in the other document.
        /// </summary>
        public int OtherStart { get; set; }

        /// <summary>
        /// Gets or sets where the run ends in the other document, exclusive.
        /// </summary>
        public int OtherEnd { get; set; }

        /// <summary>
        /// Gets or sets the run's lines, paired where a line was changed rather than replaced.
        /// </summary>
        public List<Operation> Operations { get; set; } = [];
    }

    /// <summary>
    /// One line of a change: removed (<c>-</c>), added (<c>+</c>) or changed (<c>~</c>).
    /// </summary>
    /// <param name="Kind">The kind.</param>
    /// <param name="Baseline">The line's index in the baseline, or -1.</param>
    /// <param name="Other">The line's index in the other document, or -1.</param>
    private sealed record Operation(char Kind, int Baseline, int Other);
}
