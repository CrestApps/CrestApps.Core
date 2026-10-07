using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Lists a PDF's bookmarks, adds or replaces them, removes them, or generates them from the document's
/// headings.
/// </summary>
internal sealed class AddPdfBookmarksTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.AddPdfBookmarks;

    private const int MaxBookmarks = 1000;
    private const int MaxListed = 300;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "mode": {
              "type": "string",
              "enum": ["add", "replace", "clear", "from_headings", "list"],
              "description": "add (default) appends 'bookmarks' after the existing ones; replace swaps the whole outline for 'bookmarks'; clear removes every bookmark; from_headings builds the outline from the document's headings, replacing any existing one; list shows the current bookmarks without saving anything."
            },
            "bookmarks": {
              "type": "array",
              "description": "The bookmarks, in order. Nest them with 'level' (a bookmark goes under the closest earlier one with a lower level) or with 'children'.",
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string" },
                  "page": { "type": "integer", "description": "One-based page it opens. A bookmark with children defaults to its first child's page." },
                  "level": { "type": "integer", "description": "One-based nesting level; 1 is top level." },
                  "y": { "type": "number", "description": "Optional: where the view starts, in points from the top of the page." },
                  "bold": { "type": "boolean" },
                  "italic": { "type": "boolean" },
                  "color": { "type": "string", "description": "Optional title colour, such as #1F4E79 or navy." },
                  "open": { "type": "boolean", "description": "Whether its children show expanded." },
                  "children": { "type": "array", "items": { "type": "object" } }
                },
                "required": ["title"]
              }
            },
            "max_depth": { "type": "integer", "minimum": 1, "maximum": 6, "description": "For from_headings: the deepest heading level kept. Defaults to 2." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddPdfBookmarksTool"/> class.
    /// </summary>
    public AddPdfBookmarksTool()
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
    public override string Description => "Lists, adds, replaces, clears or generates a PDF's bookmarks (the outline viewers show beside the pages). mode 'from_headings' detects headings by their font size, skipping running heads and page numbers, and builds a nested outline; 'list' only reads. Changes are saved as a working copy.";

    /// <summary>
    /// Carries out the bookmark request.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var requests = arguments.Get<List<PdfBookmarkRequest>>("bookmarks");

        if (requests is null && arguments.TryGetElement("bookmarks", out _))
        {
            throw new PdfToolException("'bookmarks' could not be read. Pass an array of objects such as [{\"title\": \"Summary\", \"page\": 1}, {\"title\": \"Details\", \"page\": 2, \"level\": 2}].");
        }

        var mode = (arguments.GetString("mode") ?? (requests is { Count: > 0 } ? "add" : "list")).Trim().ToLowerInvariant();
        var password = arguments.GetString("password");

        if (mode == "list")
        {
            var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
            var bytes = await context.ReadPdfAsync(source, cancellationToken);

            using var pig = PdfFiles.OpenForReading(bytes, password);

            var listing = PdfBookmarks.Describe(pig, MaxListed, out var total);

            if (listing is null)
            {
                return $"{PdfPropertiesToolText.Capitalize(source.Describe())} has no bookmarks. Add them with 'bookmarks', or generate them with mode 'from_headings'.";
            }

            var more = total > MaxListed
                ? $"\n({total - MaxListed:N0} more not shown.)"
                : string.Empty;

            return $"{PdfPropertiesToolText.Capitalize(source.Describe())} has {total:N0} bookmark(s):\n{listing}{more}";
        }

        if (mode is not ("add" or "replace" or "clear" or "from_headings"))
        {
            throw new PdfToolException($"'{mode}' is not a mode. Use add, replace, clear, from_headings or list.");
        }

        if ((mode is "add" or "replace") && (requests is null || requests.Count == 0))
        {
            throw new PdfToolException($"Mode '{mode}' needs 'bookmarks'. To build them from the headings use mode 'from_headings'.");
        }

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);

            using var pig = PdfFiles.OpenForReading(bytes, password);
            using var document = PdfFiles.OpenForEditing(bytes, password);

            var existing = PdfBookmarks.Count(pig);

            var signatures = PdfObjects.DescribeBrokenSignatures(document);
            var sync = PdfMetadataSync.Capture(document);
            var answer = new StringBuilder();
            List<PdfBookmarkNode> nodes = null;
            string change;
            string summary;

            switch (mode)
            {
                case "clear":
                    if (!PdfBookmarks.Clear(document) || existing == 0)
                    {
                        throw new PdfToolException($"{PdfPropertiesToolText.Capitalize(target.Describe())} has no bookmarks to clear.");
                    }

                    change = $"Removed all {existing} bookmarks";
                    summary = $"Removed all {existing:N0} bookmark(s).";

                    break;
                case "from_headings":
                    var depth = Math.Clamp(arguments.GetInt("max_depth") ?? 2, 1, 6);

                    nodes = PdfHeadingDetector.Detect(pig, depth, MaxBookmarks, out var title);

                    if (nodes.Count == 0)
                    {
                        throw new PdfToolException("No headings were found: no lines are drawn clearly larger than the body text. Pass 'bookmarks' explicitly instead (search_pdf or extract_pdf_structure can help find the sections).");
                    }

                    PdfBookmarks.Clear(document);

                    var generated = PdfBookmarks.Write(document, nodes);

                    change = $"Generated {generated} bookmarks from the headings";
                    summary = $"Generated {generated:N0} bookmark(s) from the headings (up to level {depth})" +
                        (existing > 0 ? $", replacing the {existing:N0} it had" : string.Empty) +
                        (title is null ? "." : $"; \"{title}\" was taken as the document's title and not bookmarked.");

                    break;
                default:
                    var count = 0;

                    nodes = Build(requests, pig, 1, ref count);

                    if (count > MaxBookmarks)
                    {
                        throw new PdfToolException($"That is {count:N0} bookmarks; add at most {MaxBookmarks:N0} in one call.");
                    }

                    if (mode == "replace")
                    {
                        PdfBookmarks.Clear(document);
                    }

                    var written = PdfBookmarks.Write(document, nodes);

                    change = mode == "replace"
                        ? $"Replaced the bookmarks with {written}"
                        : $"Added {written} bookmarks";
                    summary = mode == "replace"
                        ? $"Replaced the {existing:N0} existing bookmark(s) with {written:N0}."
                        : $"Added {written:N0} bookmark(s)" + (existing > 0 ? $" after the {existing:N0} it had." : ".");

                    break;
            }

            var saved = sync.Save(document, context.TimeProvider.GetUtcNow());
            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), saved, change, cancellationToken);

            answer.AppendLine(PdfPropertiesToolText.Saved(target, working));
            answer.AppendLine(summary);

            if (nodes is not null)
            {
                var lines = 0;

                AppendTree(answer, nodes, 1, ref lines);
            }

            PdfPropertiesToolText.AppendNotes(answer, signatures, PdfProtection.DescribeDropped(bytes, password));

            return answer.ToString().TrimEnd();
        }, cancellationToken);
    }

    private static List<PdfBookmarkNode> Build(List<PdfBookmarkRequest> requests, PigDocument pig, int depth, ref int count)
    {
        var roots = new List<PdfBookmarkNode>();

        if (requests is null)
        {
            return roots;
        }

        if (depth > 16)
        {
            throw new PdfToolException("The bookmarks are nested more than 16 levels deep.");
        }

        var stack = new List<(int Level, PdfBookmarkNode Node)>();

        foreach (var request in requests)
        {
            if (request is null)
            {
                continue;
            }

            var title = request.Title?.Trim();

            if (string.IsNullOrEmpty(title))
            {
                throw new PdfToolException("Every bookmark needs a 'title'.");
            }

            if (title.Length > 500)
            {
                title = title[..500];
            }

            var children = Build(request.Children, pig, depth + 1, ref count);
            var page = request.Page ?? children.FirstOrDefault()?.Page
                ?? throw new PdfToolException($"Bookmark \"{title}\" needs a 'page'.");

            if (page < 1 || page > pig.NumberOfPages)
            {
                throw new PdfToolException($"Bookmark \"{title}\" points at page {page}, but the document has {pig.NumberOfPages} page(s).");
            }

            PdfColor? color = null;

            if (!string.IsNullOrWhiteSpace(request.Color))
            {
                if (!PdfColor.TryParse(request.Color, out var parsed))
                {
                    throw new PdfToolException($"\"{request.Color}\" is not a colour. Use a hex value such as #1F4E79 or a name such as navy.");
                }

                color = parsed;
            }

            double? top = null;

            if (request.Y is { } y)
            {
                var visible = PdfBox.VisibleArea(pig.GetPage(page));

                top = visible.Top - Math.Clamp(y, 0, visible.Height);
            }

            var level = Math.Max(1, request.Level ?? 1);

            while (stack.Count > 0 && stack[^1].Level >= level)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            var node = new PdfBookmarkNode
            {
                Title = title,
                Page = page,
                Top = top,
                Bold = request.Bold == true,
                Italic = request.Italic == true,
                Color = color,
                Open = request.Open ?? (stack.Count == 0 && depth == 1),
            };

            node.Children.AddRange(children);
            count++;

            if (stack.Count == 0)
            {
                roots.Add(node);
            }
            else
            {
                stack[^1].Node.Children.Add(node);
            }

            stack.Add((level, node));
        }

        return roots;
    }

    private static void AppendTree(StringBuilder answer, List<PdfBookmarkNode> nodes, int level, ref int lines)
    {
        foreach (var node in nodes)
        {
            if (lines >= 60)
            {
                answer.AppendLine("(more bookmarks not shown; use mode 'list' to see them all)");
                lines = int.MaxValue;

                return;
            }

            answer.Append(' ', (level - 1) * 2)
                .Append("- ")
                .Append(node.Title)
                .AppendLine(string.Create(CultureInfo.InvariantCulture, $" (page {node.Page})"));
            lines++;

            AppendTree(answer, node.Children, level + 1, ref lines);

            if (lines == int.MaxValue)
            {
                return;
            }
        }
    }
}
