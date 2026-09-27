using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Intelligence;

/// <summary>
/// Reads the text of one or more PDFs page by page, and cuts it into the pieces the model-assisted tools
/// work with: chunks a model can read in one pass, and short passages a search ranks.
/// </summary>
/// <remarks>
/// Every piece keeps the document and page it came from, so whatever a model or a search says about it can
/// be cited back to a page.
/// </remarks>
internal static class PdfCorpus
{
    /// <summary>
    /// A page with fewer non-space characters than this is treated as having no text of its own.
    /// </summary>
    public const int MinimumTextCharacters = 20;

    private static readonly string[] _separators = ["\n\n", "\n", ". ", " "];

    /// <summary>
    /// Reads the text of pages in reading order.
    /// </summary>
    /// <param name="pdf">The open document.</param>
    /// <param name="document">The name the document is cited by.</param>
    /// <param name="pages">The one-based pages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One entry per page, with empty text for a page that has none or cannot be read.</returns>
    public static List<PdfCorpusPage> ReadPages(PigDocument pdf, string document, IEnumerable<int> pages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(pages);

        var result = new List<PdfCorpusPage>();

        foreach (var number in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string text;

            try
            {
                text = PdfPageText.GetText(pdf.GetPage(number));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A page PdfPig cannot parse is read as empty rather than failing the whole document.
                text = string.Empty;
            }

            result.Add(new PdfCorpusPage(document, number, text ?? string.Empty));
        }

        return result;
    }

    /// <summary>
    /// Opens one PDF and reads the text of the selected pages.
    /// </summary>
    /// <param name="context">The PDF context.</param>
    /// <param name="source">The PDF.</param>
    /// <param name="password">The password, when the PDF needs one.</param>
    /// <param name="pages">The page selection, or <see langword="null"/> for every page.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document's text.</returns>
    public static async Task<PdfCorpusDocument> LoadAsync(
        PdfToolContext context,
        PdfSource source,
        string password,
        string pages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(source);

        var bytes = await context.ReadPdfAsync(source, cancellationToken);

        using var pdf = PdfFiles.OpenForReading(bytes, password);

        var selection = PdfPageRange.Parse(pages, pdf.NumberOfPages);

        return new PdfCorpusDocument(source.Name, pdf.NumberOfPages, ReadPages(pdf, source.Name, selection, cancellationToken));
    }

    /// <summary>
    /// Opens several PDFs, skipping those that cannot be read and saying why.
    /// </summary>
    /// <param name="context">The PDF context.</param>
    /// <param name="names">The PDFs to read, or an empty list for every PDF in the conversation.</param>
    /// <param name="password">The password for protected PDFs, when the user gave one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The documents read and the ones skipped.</returns>
    public static async Task<PdfCorpusLoad> LoadManyAsync(
        PdfToolContext context,
        IReadOnlyList<string> names,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sources = names is { Count: > 0 }
            ? await FindAllAsync(context, names, cancellationToken)
            : ListAvailable(context, await context.GetStateAsync(cancellationToken));

        var documents = new List<PdfCorpusDocument>();
        var skipped = new List<string>();

        foreach (var source in sources)
        {
            try
            {
                documents.Add(await LoadAsync(context, source, password, null, cancellationToken));
            }
            catch (PdfToolException ex)
            {
                skipped.Add($"\"{source.Name}\": {ex.Message}");
            }
        }

        return new PdfCorpusLoad(documents, skipped);
    }

    /// <summary>
    /// Lists every PDF a tool call can name: the working PDFs, then the uploads.
    /// </summary>
    /// <param name="context">The PDF context.</param>
    /// <param name="state">The workspace.</param>
    /// <returns>The sources.</returns>
    public static List<PdfSource> ListAvailable(PdfToolContext context, PdfWorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);

        var sources = new List<PdfSource>();

        foreach (var working in state.Documents)
        {
            sources.Add(PdfSource.ForWorking(working));
        }

        foreach (var upload in context.PdfUploads)
        {
            sources.Add(PdfSource.ForUpload(upload));
        }

        return sources;
    }

    /// <summary>
    /// Writes the label a page's text is introduced with.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="withDocument">Whether the label names the document, as it must when several are read together.</param>
    /// <returns>For example <c>[Page 3]</c> or <c>[Document "a.pdf", page 3]</c>.</returns>
    public static string Label(PdfCorpusPage page, bool withDocument)
    {
        ArgumentNullException.ThrowIfNull(page);

        return withDocument
            ? string.Create(CultureInfo.InvariantCulture, $"[Document \"{page.Document}\", page {page.Number}]")
            : string.Create(CultureInfo.InvariantCulture, $"[Page {page.Number}]");
    }

    /// <summary>
    /// Writes a page citation.
    /// </summary>
    /// <param name="pages">The pages.</param>
    /// <returns>For example <c>p. 3</c> or <c>pp. 1-3, 5</c>; empty when there are no pages.</returns>
    public static string Cite(IEnumerable<int> pages)
    {
        var list = (pages ?? []).Distinct().Order().ToList();

        return list.Count switch
        {
            0 => string.Empty,
            1 => string.Create(CultureInfo.InvariantCulture, $"p. {list[0]}"),
            _ => "pp. " + PdfPageRange.Describe(list),
        };
    }

    /// <summary>
    /// Returns whether a text has too few characters to be a page's real text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when the text is empty or nearly so.</returns>
    public static bool IsNearlyEmpty(string text)
    {
        return string.IsNullOrEmpty(text) || text.Count(character => !char.IsWhiteSpace(character)) < MinimumTextCharacters;
    }

    /// <summary>
    /// Cuts page texts into chunks no longer than a model reads in one pass, on page boundaries where it
    /// can and on paragraph, line or sentence boundaries where a single page is too long.
    /// </summary>
    /// <param name="pages">The pages.</param>
    /// <param name="maxCharacters">The most characters one chunk holds, labels included.</param>
    /// <param name="withDocument">Whether page labels name the document.</param>
    /// <returns>The chunks, in page order; pages without text are left out.</returns>
    public static List<PdfTextChunk> Chunk(IReadOnlyList<PdfCorpusPage> pages, int maxCharacters, bool withDocument = false)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var limit = Math.Max(maxCharacters, 400);
        var chunks = new List<PdfTextChunk>();
        var builder = new StringBuilder();
        var chunkPages = new List<int>();

        void Flush()
        {
            if (builder.Length == 0)
            {
                return;
            }

            chunks.Add(new PdfTextChunk([.. chunkPages.Distinct()], builder.ToString().TrimEnd()));
            builder.Clear();
            chunkPages.Clear();
        }

        foreach (var page in pages)
        {
            var text = page.Text?.Trim();

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var label = Label(page, withDocument);
            var blockLength = label.Length + text.Length + 3;

            if (blockLength <= limit)
            {
                if (builder.Length + blockLength > limit)
                {
                    Flush();
                }

                builder.Append(label).Append('\n').Append(text).Append("\n\n");
                chunkPages.Add(page.Number);

                continue;
            }

            Flush();

            var continued = label[..^1] + ", continued]";
            var parts = Pack(text, limit - continued.Length - 3);

            for (var index = 0; index < parts.Count; index++)
            {
                builder.Append(index == 0 ? label : continued).Append('\n').Append(parts[index]).Append("\n\n");
                chunkPages.Add(page.Number);

                // The last part stays open, so the pages that follow can share its chunk.
                if (index < parts.Count - 1)
                {
                    Flush();
                }
            }
        }

        Flush();

        return chunks;
    }

    /// <summary>
    /// Formats pages as labelled text, the form a model is given a document in.
    /// </summary>
    /// <param name="pages">The pages.</param>
    /// <param name="withDocument">Whether page labels name the document.</param>
    /// <returns>The text; pages without text are left out.</returns>
    public static string Format(IEnumerable<PdfCorpusPage> pages, bool withDocument = false)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var builder = new StringBuilder();

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Text))
            {
                continue;
            }

            builder.Append(Label(page, withDocument)).Append('\n').Append(page.Text.Trim()).Append("\n\n");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Splits page texts into short, overlapping passages for a search to rank. A passage never spans two
    /// pages, so each one cites a single page.
    /// </summary>
    /// <param name="pages">The pages.</param>
    /// <param name="size">The passage length aimed for, in characters.</param>
    /// <param name="overlap">How many characters consecutive passages share, so a sentence cut at one edge is whole in the next.</param>
    /// <returns>The passages, in page order.</returns>
    public static List<PdfPassage> SplitPassages(IReadOnlyList<PdfCorpusPage> pages, int size = 700, int overlap = 150)
    {
        ArgumentNullException.ThrowIfNull(pages);

        size = Math.Max(size, 100);
        overlap = Math.Clamp(overlap, 0, size / 2);

        var passages = new List<PdfPassage>();

        foreach (var page in pages)
        {
            var text = CollapseWhitespace(page.Text);

            if (text.Length == 0)
            {
                continue;
            }

            var start = 0;

            while (start < text.Length)
            {
                var end = Math.Min(text.Length, start + size);

                if (end < text.Length)
                {
                    end = FindBreak(text, start + (size / 2), end);
                }

                var passage = text[start..end].Trim();

                if (passage.Length > 0)
                {
                    passages.Add(new PdfPassage(page.Document, page.Number, passage));
                }

                if (end >= text.Length)
                {
                    break;
                }

                var next = end - overlap;

                // The next passage starts on a word, and always moves forward.
                while (next > start && next < end && !char.IsWhiteSpace(text[next - 1]))
                {
                    next++;
                }

                start = next <= start
                    ? end
                    : next;
            }
        }

        return passages;
    }

    /// <summary>
    /// Collapses every run of whitespace, line breaks included, to a single space.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The collapsed text, trimmed.</returns>
    public static string CollapseWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;

                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Cuts a text into parts no longer than a limit, preferring paragraph, then line, then sentence, then
    /// word boundaries.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="limit">The most characters in a part.</param>
    /// <returns>The parts.</returns>
    public static List<string> Pack(string text, int limit)
    {
        var parts = new List<string>();

        Pack(text ?? string.Empty, Math.Max(limit, 50), 0, parts);

        return parts;
    }

    private static void Pack(string text, int limit, int level, List<string> parts)
    {
        if (text.Length <= limit)
        {
            if (text.Trim().Length > 0)
            {
                parts.Add(text.Trim());
            }

            return;
        }

        if (level >= _separators.Length)
        {
            for (var start = 0; start < text.Length; start += limit)
            {
                parts.Add(text.Substring(start, Math.Min(limit, text.Length - start)));
            }

            return;
        }

        var separator = _separators[level];
        var pieces = text.Split(separator);

        if (pieces.Length == 1)
        {
            Pack(text, limit, level + 1, parts);

            return;
        }

        var builder = new StringBuilder();

        foreach (var piece in pieces)
        {
            if (piece.Length > limit)
            {
                if (builder.Length > 0)
                {
                    Pack(builder.ToString(), limit, level + 1, parts);
                    builder.Clear();
                }

                Pack(piece, limit, level + 1, parts);

                continue;
            }

            if (builder.Length > 0 && builder.Length + separator.Length + piece.Length > limit)
            {
                parts.Add(builder.ToString().Trim());
                builder.Clear();
            }

            if (builder.Length > 0)
            {
                builder.Append(separator);
            }

            builder.Append(piece);
        }

        if (builder.Length > 0 && builder.ToString().Trim().Length > 0)
        {
            parts.Add(builder.ToString().Trim());
        }
    }

    private static int FindBreak(string text, int from, int to)
    {
        // A sentence end is the best place to cut, a word boundary the next best.
        for (var index = to; index > from; index--)
        {
            if (index < text.Length && text[index - 1] is '.' or '!' or '?' or ';' && char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        for (var index = to; index > from; index--)
        {
            if (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        return to;
    }

    private static async Task<List<PdfSource>> FindAllAsync(PdfToolContext context, IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        var sources = new List<PdfSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            var source = await context.FindPdfAsync(name, cancellationToken);

            if (seen.Add((source.IsUpload ? "upload:" : "working:") + source.Name))
            {
                sources.Add(source);
            }
        }

        return sources;
    }
}
