using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Intelligence;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Finds the passages of one or more PDFs that answer a question, ranked with BM25 and cited by document and
/// page, and optionally writes an answer grounded in them.
/// </summary>
internal sealed class AskPdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.AskPdf;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            "question": {
              "type": "string",
              "description": "The question to answer from the PDFs, in the user's words."
            },
            "pdfs": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Optional PDFs to search: working PDF names or uploaded file names. Omit to search every PDF in the conversation."
            },
            "top_k": {
              "type": "integer",
              "description": "How many passages to return, 1 to 20. Defaults to 6."
            },
            "answer": {
              "type": "boolean",
              "description": "Also write an answer grounded in the passages, with citations. Defaults to false: the passages are returned for you to answer from."
            },
            {{PdfToolSchemas.Password}}
          },
          "required": ["question"],
          "additionalProperties": false
        }
        """;

    private const string Instructions = """
        You answer questions about documents using only the numbered passages supplied.
        - Cite every statement with the document and page of the passage it comes from: (p. 4) when there is one document, or ("file.pdf", p. 4) when there are several.
        - Quote numbers, dates and names exactly as written.
        - If the passages do not contain the answer, say "The documents do not say." and then mention any related information they do contain.
        - Never use outside knowledge and never guess.
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AskPdfTool"/> class.
    /// </summary>
    public AskPdfTool()
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
    public override string Description => "Answers a question from one or more PDFs: finds the passages most relevant to 'question' (ranked by keyword relevance) and returns them with their document and page number, so the answer can cite pages as (p. 4). Searches every PDF in the conversation unless 'pdfs' names some. With answer=true it also writes an answer grounded in those passages. Use search_pdf instead to find an exact phrase.";

    /// <summary>
    /// Finds the passages that answer the question.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The passages, and the answer when asked for.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var question = arguments.GetString("question")
            ?? throw new PdfToolException("Pass 'question': what the user wants to know from the PDFs.");

        var names = arguments.GetStrings("pdfs");

        if (names.Count == 0 && arguments.Pdf() is { } single)
        {
            names.Add(single);
        }

        var topK = Math.Clamp(arguments.GetInt("top_k") ?? 6, 1, 20);
        var load = await PdfCorpus.LoadManyAsync(context, names, arguments.GetString("password"), cancellationToken);

        if (load.Documents.Count == 0)
        {
            throw new PdfToolException(load.Skipped.Count > 0
                ? "None of the PDFs could be read. " + string.Join(" ", load.Skipped)
                : "There are no PDFs in this conversation to search. Ask the user to upload one.");
        }

        var passages = PdfCorpus.SplitPassages([.. load.Documents.SelectMany(document => document.Pages)]);

        if (passages.Count == 0)
        {
            return "The PDFs searched have no extractable text; they are probably scanned. Run ocr_pdf to read them first." + Skipped(load);
        }

        var index = new PdfBm25Index(passages.Select(passage => passage.Text));
        var hits = Select(index.Search(question, topK * 3), passages, topK);
        var several = load.Documents.Count > 1;
        var builder = new StringBuilder();

        builder.Append("Question: \"").Append(question).Append("\"\nSearched ")
            .Append(string.Join(", ", load.Documents.Select(document => string.Create(CultureInfo.InvariantCulture, $"\"{document.Name}\" ({document.PageCount} pages)"))))
            .Append('.');

        if (hits.Count == 0)
        {
            builder.Append("\nNo passage shares a meaningful word with the question. Rephrase it with words the document is likely to use, or use search_pdf for an exact phrase.");

            return builder.Append(Skipped(load)).ToString();
        }

        builder.Append(' ').Append(hits.Count.ToString(CultureInfo.InvariantCulture)).Append(" most relevant passage(s), best first.\n");

        if (arguments.GetBoolean("answer") == true)
        {
            var model = await PdfModelClient.ResolveTextAsync(context.Services);

            if (model is null)
            {
                builder.Append("\nNo AI model is configured to write the answer on this host; answer from the passages below yourself.\n");
            }
            else
            {
                var answer = await model.CompleteAsync(Instructions, BuildRequest(question, hits, several), 1200, cancellationToken);
                var text = string.IsNullOrWhiteSpace(answer)
                    ? "(The model returned no answer; answer from the passages yourself.)"
                    : answer.Trim();

                builder.Append("\nAnswer (grounded in the passages below):\n").Append(text).Append('\n');
            }
        }

        builder.Append(several
            ? "\nCite the document and page of every fact you use, as (\"file.pdf\", p. 4). "
            : "\nCite the page of every fact you use, as (p. 4). ");

        builder.Append("If the passages do not answer the question, say so rather than guessing.\n");

        for (var number = 0; number < hits.Count; number++)
        {
            var (passage, score, alsoIn) = hits[number];

            builder.Append('\n').Append('[').Append((number + 1).ToString(CultureInfo.InvariantCulture)).Append("] \"")
                .Append(passage.Document).Append("\", page ").Append(passage.Page.ToString(CultureInfo.InvariantCulture))
                .Append(" (relevance ").Append(score.ToString("0.0", CultureInfo.InvariantCulture)).Append(')');

            if (alsoIn.Count > 0)
            {
                builder.Append(", also in ").Append(string.Join(", ", alsoIn));
            }

            builder.Append('\n').Append(passage.Text).Append('\n');
        }

        return builder.Append(Skipped(load)).ToString().TrimEnd();
    }

    private static List<(PdfPassage Passage, double Score, List<string> AlsoIn)> Select(List<PdfBm25Hit> ranked, List<PdfPassage> passages, int topK)
    {
        var selected = new List<(PdfPassage Passage, double Score, List<string> AlsoIn)>();
        var byText = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var hit in ranked)
        {
            var passage = passages[hit.Index];

            // A working copy repeats the upload it was made from; the same passage is shown once.
            if (byText.TryGetValue(passage.Text, out var existing))
            {
                var alsoIn = selected[existing].AlsoIn;
                var reference = string.Create(CultureInfo.InvariantCulture, $"\"{passage.Document}\" p. {passage.Page}");

                if (!alsoIn.Contains(reference) && !string.Equals(selected[existing].Passage.Document, passage.Document, StringComparison.Ordinal))
                {
                    alsoIn.Add(reference);
                }

                continue;
            }

            if (selected.Count >= topK)
            {
                continue;
            }

            byText[passage.Text] = selected.Count;
            selected.Add((passage, hit.Score, []));
        }

        return selected;
    }

    private static string BuildRequest(string question, List<(PdfPassage Passage, double Score, List<string> AlsoIn)> hits, bool several)
    {
        var builder = new StringBuilder();

        builder.Append("Question: ").Append(question).Append("\n\nPassages:\n");

        for (var number = 0; number < hits.Count; number++)
        {
            var passage = hits[number].Passage;

            builder.Append('[').Append((number + 1).ToString(CultureInfo.InvariantCulture)).Append("] ");

            if (several)
            {
                builder.Append("Document \"").Append(passage.Document).Append("\", ");
            }

            builder.Append("page ").Append(passage.Page.ToString(CultureInfo.InvariantCulture)).Append(":\n")
                .Append(passage.Text).Append("\n\n");
        }

        return builder.ToString().TrimEnd();
    }

    private static string Skipped(PdfCorpusLoad load)
    {
        return load.Skipped.Count == 0
            ? string.Empty
            : "\n\nNot searched: " + string.Join(" ", load.Skipped);
    }
}
