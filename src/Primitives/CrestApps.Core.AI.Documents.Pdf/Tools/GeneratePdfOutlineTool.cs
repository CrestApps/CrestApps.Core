using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Builds a PDF's outline — its sections and where they start — from the bookmarks the document carries,
/// or, when it has none, from the headings its type sizes set apart.
/// </summary>
internal sealed class GeneratePdfOutlineTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.GeneratePdfOutline;

    private const int MaxHeadingPages = 300;
    private const int MaxEntries = 1_000;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            "source": {
              "type": "string",
              "enum": ["auto", "bookmarks", "headings"],
              "description": "Where the outline comes from: 'auto' (default) uses the bookmarks when the PDF has them and detected headings otherwise; 'bookmarks' only the bookmarks; 'headings' headings detected from type sizes even when bookmarks exist."
            },
            "max_depth": {
              "type": "integer",
              "description": "The deepest level included, 1-6. Defaults to 3."
            },
            "format": {
              "type": "string",
              "enum": ["text", "json"],
              "description": "'text' (default) as an indented list, or 'json' as nested entries."
            }
          },
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneratePdfOutlineTool"/> class.
    /// </summary>
    public GeneratePdfOutlineTool()
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
    public override string Description => "Builds a PDF's outline (table of contents) as a nested list of titles with page numbers: from the document's bookmarks when it has them, otherwise from headings detected by type size (running headers and footers are ignored). Use it to navigate a long document, cite sections or plan bookmarks.";

    /// <summary>
    /// Builds the outline.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The conversation's PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outline.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var from = (arguments.GetString("source") ?? "auto").Trim().ToLowerInvariant();

        if (from is not ("auto" or "bookmarks" or "headings"))
        {
            throw new PdfToolException($"\"{from}\" is not an outline source. Use 'auto', 'bookmarks' or 'headings'.");
        }

        var format = (arguments.GetString("format") ?? "text").Trim().ToLowerInvariant();

        if (format is not ("text" or "json"))
        {
            throw new PdfToolException($"\"{format}\" is not an outline format. Use 'text' or 'json'.");
        }

        var maxDepth = Math.Clamp(arguments.GetInt("max_depth") ?? 3, 1, 6);

        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        using var pdf = PdfFiles.OpenForReading(bytes, arguments.GetString("password"));

        List<OutlineEntry> entries = null;
        string origin;
        var notes = new List<string>();

        if (from != "headings")
        {
            entries = ReadBookmarks(pdf, maxDepth, notes);
        }

        if (entries is { Count: > 0 })
        {
            origin = "from its bookmarks";
        }
        else if (from == "bookmarks")
        {
            return $"\"{source.Name}\" has no bookmarks. Ask for source 'headings' to build an outline from its headings, or add bookmarks with add_pdf_bookmarks.";
        }
        else
        {
            var pages = Enumerable.Range(1, Math.Min(pdf.NumberOfPages, MaxHeadingPages)).ToList();
            var layout = PdfLayoutAnalyzer.Analyze(pdf, pages, content: null, encodeImages: false, cancellationToken);

            entries = BuildFromHeadings(layout, maxDepth);
            origin = from == "auto"
                ? "from headings detected by type size (it has no bookmarks)"
                : "from headings detected by type size";

            if (layout.BodyFontSize > 0)
            {
                var tiers = layout.HeadingSizes.Select((size, index) => FormattableString.Invariant($"level {index + 1} = {size:0.#}pt"));

                notes.Add(FormattableString.Invariant($"Body text is {layout.BodyFontSize:0.#}pt") + (layout.HeadingSizes.Count > 0 ? "; " + string.Join(", ", tiers) : string.Empty) + ". Bold lines in body-size type count as the lowest level.");
            }

            if (pdf.NumberOfPages > MaxHeadingPages)
            {
                notes.Add(FormattableString.Invariant($"Only the first {MaxHeadingPages} of {pdf.NumberOfPages} pages were read for headings."));
            }
        }

        if (entries.Count == 0)
        {
            return $"No outline could be built for \"{source.Name}\": it has no bookmarks and no text set apart as headings. extract_pdf_structure shows how its text is organised.";
        }

        var count = Count(entries);
        var writer = new PdfResponseWriter(context.Options.MaxToolResponseCharacters);

        writer.Line(FormattableString.Invariant($"Outline of \"{source.Name}\" {origin}, {count} entr{(count == 1 ? "y" : "ies")} to depth {maxDepth}:"));

        foreach (var note in notes)
        {
            writer.Line(note);
        }

        writer.Line();

        if (format == "json")
        {
            var json = PdfReadingJson.Serialize(entries.Select(ToJson).ToList());

            if (!writer.TryLine(json))
            {
                writer.Line("[The outline is too long to return as JSON; ask for format 'text' or a smaller max_depth.]");
            }
        }
        else if (!WriteText(writer, entries, 0))
        {
            writer.Line("[The outline was cut to stay within the answer size; ask for a smaller max_depth.]");
        }

        return writer.ToString();
    }

    private static List<OutlineEntry> ReadBookmarks(PdfDocument pdf, int maxDepth, List<string> notes)
    {
        try
        {
            if (!pdf.TryGetBookmarks(out var bookmarks) || bookmarks is null || bookmarks.Roots.Count == 0)
            {
                return [];
            }

            var deeper = 0;
            var budget = MaxEntries;
            var entries = ConvertNodes(bookmarks.Roots, 1, maxDepth, ref deeper, ref budget);

            if (deeper > 0)
            {
                notes.Add(FormattableString.Invariant($"{deeper} bookmark(s) deeper than level {maxDepth} were left out; raise max_depth to see them."));
            }

            return entries;
        }
        catch (Exception)
        {
            // Bookmarks that cannot be read are no bookmarks; the headings still make an outline.
            return [];
        }
    }

    private static List<OutlineEntry> ConvertNodes(IReadOnlyList<BookmarkNode> nodes, int depth, int maxDepth, ref int deeper, ref int budget)
    {
        var entries = new List<OutlineEntry>();

        foreach (var node in nodes)
        {
            if (depth > maxDepth)
            {
                deeper += 1 + CountNodes(node.Children);

                continue;
            }

            if (budget-- <= 0)
            {
                break;
            }

            var entry = new OutlineEntry
            {
                Title = PdfTextPatterns.OneLine(node.Title),
                Level = depth,
                Page = node switch
                {
                    DocumentBookmarkNode document when document.PageNumber > 0 => document.PageNumber,
                    _ => null,
                },
                Link = node is UriBookmarkNode uri ? uri.Uri : null,
            };

            entry.Children.AddRange(ConvertNodes(node.Children, depth + 1, maxDepth, ref deeper, ref budget));

            // A grouping entry that points nowhere takes the page of its first child, as viewers show it.
            if (entry.Page is null && entry.Link is null && entry.Children.Count > 0)
            {
                entry.Page = entry.Children[0].Page;
            }

            entries.Add(entry);
        }

        return entries;
    }

    private static int CountNodes(IReadOnlyList<BookmarkNode> nodes)
    {
        return nodes.Count + nodes.Sum(node => CountNodes(node.Children));
    }

    private static List<OutlineEntry> BuildFromHeadings(PdfDocumentLayout layout, int maxDepth)
    {
        var roots = new List<OutlineEntry>();
        var stack = new List<OutlineEntry>();
        var added = 0;

        foreach (var page in layout.Pages)
        {
            foreach (var block in page.Blocks)
            {
                if (block.Role != PdfLayoutRoles.Heading || added >= MaxEntries)
                {
                    continue;
                }

                var level = block.Level ?? 1;

                while (stack.Count > 0 && stack[^1].Level >= level)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                // Depth in the outline, not the type tier: a level skipped in the document is not a gap here.
                if (stack.Count + 1 > maxDepth)
                {
                    continue;
                }

                var entry = new OutlineEntry
                {
                    Title = PdfTextPatterns.OneLine(block.Text),
                    Level = level,
                    Page = page.PageNumber,
                };

                if (stack.Count == 0)
                {
                    roots.Add(entry);
                }
                else
                {
                    stack[^1].Children.Add(entry);
                }

                stack.Add(entry);
                added++;
            }
        }

        return roots;
    }

    private static int Count(List<OutlineEntry> entries)
    {
        return entries.Count + entries.Sum(entry => Count(entry.Children));
    }

    private static bool WriteText(PdfResponseWriter writer, List<OutlineEntry> entries, int depth)
    {
        foreach (var entry in entries)
        {
            var target = entry.Page is int page
                ? " (p. " + page.ToString(CultureInfo.InvariantCulture) + ")"
                : string.Empty;

            if (entry.Link is not null)
            {
                target += " (link: " + entry.Link + ")";
            }

            if (!writer.TryLine(new string(' ', depth * 2) + "- " + entry.Title + target))
            {
                return false;
            }

            if (!WriteText(writer, entry.Children, depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    private static object ToJson(OutlineEntry entry)
    {
        return new
        {
            title = entry.Title,
            page = entry.Page,
            link = entry.Link,
            children = entry.Children.Count > 0 ? entry.Children.Select(ToJson).ToList() : null,
        };
    }

    /// <summary>
    /// One entry of an outline.
    /// </summary>
    private sealed class OutlineEntry
    {
        /// <summary>
        /// Gets or sets the title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the level: a bookmark's depth, or a heading's type tier.
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// Gets or sets the one-based page the entry leads to.
        /// </summary>
        public int? Page { get; set; }

        /// <summary>
        /// Gets or sets the web address a bookmark opens instead of a page.
        /// </summary>
        public string Link { get; set; }

        /// <summary>
        /// Gets the entries nested under this one.
        /// </summary>
        public List<OutlineEntry> Children { get; } = [];
    }
}
