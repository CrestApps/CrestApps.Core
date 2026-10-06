using System.Text;
using CrestApps.Core.AI.Documents.Word.Rendering;
using CrestApps.Core.AI.Documents.Word.Workspace;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Shows pages of a Word document in the conversation as pictures.
/// </summary>
/// <remarks>
/// The pages are drawn from the same file an export writes, after the same refresh of its table of contents,
/// captions and references, so what the reader approves in the preview is what they download. The number of
/// pages drawn per call is bounded and the answer says which were left out.
/// </remarks>
internal sealed class PreviewWordTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.PreviewWord;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "pages": { "type": "string", "description": "Pages to show, one-based: \"1\", \"2-3\", \"1,4\", \"last\". Defaults to the first pages, up to the preview limit." },
            "show_markup": { "type": "boolean", "description": "Draw tracked changes as markup (insertions underlined, deletions struck through) instead of the final text." },
            "format": { "type": "string", "enum": ["image", "text"], "description": "'image' (default) draws the pages; 'text' writes their text instead, only when the user asks for text." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreviewWordTool"/> class.
    /// </summary>
    public PreviewWordTool()
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
    public override string Description => "Shows pages of a Word document in the conversation as pictures, drawn from the same file export_word writes, so the user sees what they will download. Use it after creating or changing a document and before exporting, and whenever the user asks to see a document or a page. A few pages are drawn per call; ask for others with 'pages'. Returns [fig:N] markers that MUST be written in the answer exactly as given. This is not a download: use export_word for the file.";

    /// <summary>
    /// Draws the pages.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindDocumentAsync(arguments.Document(), cancellationToken);
        var layout = await WordPreview.LayoutAsync(context, source, arguments.GetBoolean("show_markup") == true, cancellationToken);

        if (layout.PageCount == 0)
        {
            return $"{source.Describe()} has no pages to show.";
        }

        var options = WordPreview.Options(context.Services);
        var maxPages = Math.Max(1, options.MaxPages);
        var requested = arguments.GetPages() is { } pages
            ? WordPageRange.Parse(pages, layout.PageCount)
            : [.. Enumerable.Range(1, Math.Min(layout.PageCount, maxPages))];

        if (requested.Count == 0)
        {
            throw new WordToolException($"The document has {layout.PageCount} page(s); ask for pages between 1 and {layout.PageCount}.");
        }

        var shown = requested.Take(maxPages).ToList();
        var omitted = requested.Skip(maxPages).ToList();
        var asText = string.Equals(arguments.GetString("format"), "text", StringComparison.OrdinalIgnoreCase);
        var markers = asText ? null : await WordPreview.ShowAsync(context, source, layout, shown, options.PageWidthPixels, highlight: null, cancellationToken);

        var answer = markers is null
            ? DescribeText(layout, shown, source, asText ? "The user asked for text." : "This host cannot show pictures here, so the pages are written out as text instead.")
            : WordPreview.DescribeShown(markers, layout, shown, source.Describe());

        if (omitted.Count > 0)
        {
            answer.Append("Not shown: pages ").Append(WordPageRange.Describe(omitted)).AppendLine(". Say so, and call preview_word with 'pages' to show them.");
        }
        else if (layout.PageCount > shown.Count)
        {
            answer.Append("The document has ").Append(layout.PageCount).AppendLine(" pages; pass 'pages' to show others.");
        }

        if (layout.Truncated)
        {
            answer.AppendLine("The document is longer than the preview lays out; later pages are not counted.");
        }

        answer.Append("Pages are laid out by this host to match Word as closely as it can; Word may break lines and pages slightly differently, especially with fonts this host does not have. The preview is not a download.");

        return answer.ToString();
    }

    private static StringBuilder DescribeText(WordLayout layout, List<int> pages, WordSource source, string reason)
    {
        var builder = new StringBuilder();

        builder.Append(reason).Append(" Text of ").Append(source.Describe()).Append(", ").Append(layout.PageCount).AppendLine(" page(s):");

        foreach (var number in pages)
        {
            var text = WordPreview.PageText(layout.Pages[number - 1]);

            builder.AppendLine().Append("--- Page ").Append(number).AppendLine(" ---");
            builder.AppendLine(string.IsNullOrWhiteSpace(text) ? "(no text on this page)" : text);
        }

        builder.AppendLine();

        return builder;
    }
}
