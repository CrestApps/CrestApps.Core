using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Intelligence;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Summarizes a PDF, or selected pages of it, with page citations; a long document is summarized in parts
/// and the parts combined.
/// </summary>
internal sealed class SummarizePdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.SummarizePdf;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "length": {
              "type": "string",
              "enum": ["short", "medium", "detailed"],
              "description": "How long the summary is: short (a few key points), medium (the default) or detailed (section by section)."
            },
            "focus": {
              "type": "string",
              "description": "Optional aspect to concentrate on, for example \"obligations of the supplier\" or \"financial results\"."
            },
            "style": {
              "type": "string",
              "enum": ["bullets", "paragraphs"],
              "description": "Bullet points (the default) or paragraphs."
            }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    private const string Instructions = """
        You summarize documents accurately and neutrally.
        - Use only the document text supplied. Never add facts, figures, names or conclusions that are not in it.
        - Each page's text starts with a label such as [Page 3]. Cite the page every point comes from as (p. 3), or (pp. 3-4) when it spans pages.
        - Keep numbers, dates and names exactly as written.
        - If the text is garbled, empty or unrelated to the request, say so instead of guessing.
        """;

    // Past this many parts a document is summarized in more than one call, so a single call stays quick.
    private const int MaxParts = 16;

    /// <summary>
    /// Initializes a new instance of the <see cref="SummarizePdfTool"/> class.
    /// </summary>
    public SummarizePdfTool()
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
    public override string Description => "Summarizes a PDF or selected pages with page citations such as (p. 4). Choose 'length' (short, medium, detailed), 'style' (bullets or paragraphs) and an optional 'focus'. Long documents are summarized in parts and combined. Scanned pages without text are left out; run ocr_pdf first to include them.";

    /// <summary>
    /// Summarizes the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var document = await PdfCorpus.LoadAsync(context, source, arguments.GetString("password"), arguments.GetPages(), cancellationToken);
        var empty = document.Pages.Where(page => PdfCorpus.IsNearlyEmpty(page.Text)).Select(page => page.Number).ToList();

        if (document.Pages.All(page => string.IsNullOrWhiteSpace(page.Text)))
        {
            return $"The selected pages of {source.Describe()} have no extractable text; they are probably scanned. Run ocr_pdf to read them first.";
        }

        var model = await PdfModelClient.ResolveTextAsync(context.Services);

        if (model is null)
        {
            return "No AI model is configured for summaries on this host; use extract_pdf_text and summarize the returned text yourself, citing pages as (p. N).";
        }

        var length = (arguments.GetString("length") ?? "medium").Trim().ToLowerInvariant();
        var style = (arguments.GetString("style") ?? "bullets").Trim().ToLowerInvariant();
        var focus = arguments.GetString("focus");
        var guidance = BuildGuidance(length, style, focus);
        var maxTokens = length switch
        {
            "short" => 500,
            "detailed" => 2400,
            _ => 1000,
        };

        var chunks = PdfCorpus.Chunk(document.Pages, context.Options.MaxModelInputCharacters);
        var covered = chunks.Take(MaxParts).ToList();
        var uncovered = chunks.Skip(MaxParts).SelectMany(chunk => chunk.Pages).Distinct().ToList();
        var pagesCovered = covered.SelectMany(chunk => chunk.Pages).Distinct().ToList();
        string summary;

        if (covered.Count == 1)
        {
            var request = $"Summarize the document \"{source.Name}\" ({Describe(pagesCovered)}).\n{guidance}\n\n--- DOCUMENT TEXT ---\n{covered[0].Text}\n--- END OF DOCUMENT TEXT ---";

            summary = await model.CompleteAsync(Instructions, request, maxTokens, cancellationToken);
        }
        else
        {
            summary = await SummarizeInPartsAsync(model, source.Name, covered, guidance, focus, maxTokens, context.Options.MaxModelInputCharacters, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            return "The model returned no summary. Use extract_pdf_text and summarize the returned text yourself.";
        }

        var builder = new StringBuilder();

        builder.Append("Summary of ").Append(source.Describe()).Append(", ").Append(Describe(pagesCovered));

        if (covered.Count > 1)
        {
            builder.Append(", summarized in ").Append(covered.Count.ToString(CultureInfo.InvariantCulture)).Append(" parts");
        }

        builder.Append(":\n\n").Append(summary.Trim()).Append('\n');

        var skippedEmpty = empty.Where(page => !uncovered.Contains(page)).ToList();

        if (skippedEmpty.Count > 0)
        {
            builder.Append("\nPages ").Append(PdfPageRange.Describe(skippedEmpty))
                .Append(" have little or no extractable text (probably scanned), so the summary may miss what they show; run ocr_pdf to read them.\n");
        }

        if (uncovered.Count > 0)
        {
            builder.Append("\nThe document is long: pages ").Append(PdfPageRange.Describe(uncovered))
                .Append(" are not in this summary. Call summarize_pdf again with 'pages' set to them.\n");
        }

        return builder.ToString().TrimEnd();
    }

    private static async Task<string> SummarizeInPartsAsync(
        PdfModelClient model,
        string name,
        List<PdfTextChunk> chunks,
        string guidance,
        string focus,
        int maxTokens,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        var focusNote = string.IsNullOrWhiteSpace(focus)
            ? string.Empty
            : $" Concentrate on: {focus}.";

        var notes = await PdfModelClient.MapAsync(
            chunks,
            (chunk, token) => model.CompleteAsync(
                Instructions,
                $"Write concise notes on this part of \"{name}\" ({Describe(chunk.Pages)}): its key facts, figures, names, dates, decisions and conclusions, each with its page citation.{focusNote} At most 250 words.\n\n--- DOCUMENT TEXT ---\n{chunk.Text}\n--- END OF DOCUMENT TEXT ---",
                700,
                token),
            cancellationToken);

        var parts = notes
            .Select((text, index) => $"[Notes on {Describe(chunks[index].Pages)}]\n{text?.Trim()}")
            .ToList();

        // Notes that together are still too long for one pass are combined in groups first.
        for (var round = 0; round < 4 && parts.Sum(part => part.Length + 2) > maxCharacters && parts.Count > 1; round++)
        {
            var groups = Group(parts, maxCharacters);

            var combined = await PdfModelClient.MapAsync(
                groups,
                (group, token) => model.CompleteAsync(
                    Instructions,
                    $"Combine these consecutive notes on \"{name}\" into shorter notes that keep every page citation.{focusNote}\n\n{group}",
                    900,
                    token),
                cancellationToken);

            parts = [.. combined.Select(text => text?.Trim() ?? string.Empty)];
        }

        var request = $"These are notes on consecutive parts of the document \"{name}\", in order, with page citations. Combine them into one summary of the whole document.\n{guidance}\nKeep the page citations from the notes.\n\n{string.Join("\n\n", parts)}";

        return await model.CompleteAsync(Instructions, request, maxTokens, cancellationToken);
    }

    private static List<string> Group(List<string> parts, int maxCharacters)
    {
        var groups = new List<string>();
        var builder = new StringBuilder();

        foreach (var part in parts)
        {
            if (builder.Length > 0 && builder.Length + part.Length + 2 > maxCharacters)
            {
                groups.Add(builder.ToString());
                builder.Clear();
            }

            builder.Append(part).Append("\n\n");
        }

        if (builder.Length > 0)
        {
            groups.Add(builder.ToString());
        }

        return groups;
    }

    private static string BuildGuidance(string length, string style, string focus)
    {
        var builder = new StringBuilder();

        builder.Append(length switch
        {
            "short" => "Length: short — the 3 to 5 most important points, under 120 words in total.",
            "detailed" => "Length: detailed — a thorough summary of about 500 to 800 words, following the document's own sections.",
            _ => "Length: medium — about 200 to 300 words covering the main points.",
        });

        builder.Append('\n').Append(style == "paragraphs"
            ? "Style: write in paragraphs, without bullet points."
            : "Style: use bullet points (\"- \"), one point per bullet.");

        if (!string.IsNullOrWhiteSpace(focus))
        {
            builder.Append("\nFocus: concentrate on ").Append(focus.Trim()).Append("; mention other content only briefly, and say so if the document does not address it.");
        }

        return builder.ToString();
    }

    private static string Describe(IEnumerable<int> pages)
    {
        var list = pages.ToList();

        return list.Count == 1
            ? string.Create(CultureInfo.InvariantCulture, $"page {list[0]}")
            : "pages " + PdfPageRange.Describe(list);
    }
}
