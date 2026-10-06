using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Rendering;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Shows PDF pages in the conversation as pictures.
/// </summary>
/// <remarks>
/// The pages are drawn from the very bytes an export would write — a composed document is rendered, and the
/// rendering is what is drawn — so what the reader approves in the preview is the file they download. The
/// number of pages drawn per call is bounded and the answer says which pages were left out, because a stack
/// of page images large enough to hold a whole report is scaled down until nothing on it can be read.
/// </remarks>
internal sealed class PreviewPdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.PreviewPdf;

    private const string CacheKey = nameof(PreviewPdfTool) + ".Responses";

    // The pages shown in this turn, by document version and page, with the marker each was shown with.
    private const string ShownPagesKey = nameof(PreviewPdfTool) + ".ShownPages";

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            "pages": { "type": "string", "description": "Pages to show, one-based: \"1\", \"2-3\", \"last\". Defaults to the first pages, up to the preview limit." },
            {{PdfToolSchemas.Password}},
            "format": { "type": "string", "enum": ["image", "text"], "description": "'image' (default) draws the pages; 'text' writes their text instead, only when the user asks for text." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreviewPdfTool"/> class.
    /// </summary>
    public PreviewPdfTool()
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
    public override string Description => "Shows pages of a PDF in the conversation as pictures, drawn from the same file an export writes, so the user sees exactly what they will download. Use it after building or changing a document and before exporting, and whenever the user asks to see a PDF or a page. A few pages are drawn per call; ask for others with 'pages'. Returns [fig:N] markers that MUST be written in the answer exactly as given. This is not a download: use export_pdf for the file.";

    /// <summary>
    /// Draws the pages.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var options = arguments.Services.GetService<IOptions<PdfPreviewOptions>>()?.Value ?? new PdfPreviewOptions();
        var asText = string.Equals(arguments.GetString("format"), "text", StringComparison.OrdinalIgnoreCase);
        var key = string.Join(
            '|',
            source.Name,
            source.Working?.Version.ToString(CultureInfo.InvariantCulture) ?? source.Upload?.ItemId,
            arguments.GetPages() ?? string.Empty,
            asText ? "text" : "image");

        if (TryGetCached(key, out var cached))
        {
            // Asked again in the same turn: the same pictures are shown again rather than drawn twice.
            foreach (var marker in AIInvocationScope.Current.ToolReferences.Keys)
            {
                if (cached.Contains(marker, StringComparison.Ordinal))
                {
                    AIInvocationScope.Current.RequestFigureDisplay(marker);
                }
            }

            return cached;
        }

        var bytes = await context.ReadPdfAsync(source, cancellationToken);

        var password = arguments.GetString("password");

        using var pdf = PdfFiles.OpenForReading(bytes, password);

        var pageCount = pdf.NumberOfPages;
        var maxPages = Math.Max(1, options.MaxPages);
        var missing = new List<int>();

        // A preview only reads, so pages asked for past the end are left out and named rather than failing
        // the call: a model that expected three pages of a two-page document still shows the two.
        var requested = arguments.GetPages() is null
            ? [.. Enumerable.Range(1, Math.Min(pageCount, maxPages))]
            : PdfPageRange.Parse(arguments.GetPages(), pageCount, missing: missing);
        var shown = requested.Take(maxPages).ToList();
        var omitted = requested.Skip(maxPages).ToList();

        string response;

        if (asText)
        {
            response = BuildTextResponse(pdf, source, shown, omitted, pageCount, "The user asked for text.");
        }
        else
        {
            // A page already shown in this turn keeps the marker it was shown with. Drawing it again under a
            // new marker is how one answer ends up with every page twice: a second preview of the same pages,
            // asked with other arguments, would hand the model a second set of markers to write.
            var shownPages = ShownPages();
            var pagePrefix = string.Join('|', source.Name, source.Working?.Version.ToString(CultureInfo.InvariantCulture) ?? source.Upload?.ItemId, string.Empty);
            var toDraw = shown.Where(page => !shownPages.ContainsKey(pagePrefix + page.ToString(CultureInfo.InvariantCulture))).ToList();
            var markers = new List<string>();
            var drawn = new List<PdfPageRendering>();

            if (toDraw.Count > 0)
            {
                // The renderer opens the file without a password, so a protected upload is drawn from a decrypted copy.
                drawn = PdfPageSvgRenderer.Render(PdfReadableCopy.WithoutPassword(bytes, password, pdf.IsEncrypted), toDraw, options);

                var safeName = PdfToolContext.SanitizeName(source.Name).Replace(' ', '_');
                var figures = drawn
                    .Select(rendering => new PdfFigure(
                        string.Create(CultureInfo.InvariantCulture, $"{source.Name} — page {rendering.PageNumber} of {pageCount}"),
                        string.Create(CultureInfo.InvariantCulture, $"{safeName}-page-{rendering.PageNumber}.svg"),
                        Encoding.UTF8.GetBytes(rendering.Svg)))
                    .ToList();

                markers = await context.ShowFiguresAsync(figures, cancellationToken);
            }

            if (markers is null)
            {
                response = BuildTextResponse(pdf, source, shown, omitted, pageCount, "This host cannot show pictures here, so the pages are written out as text instead.");
            }
            else
            {
                for (var index = 0; index < drawn.Count; index++)
                {
                    // The picture itself is stored; only what describes it is kept for the rest of the turn.
                    shownPages[pagePrefix + drawn[index].PageNumber.ToString(CultureInfo.InvariantCulture)] = (markers[index], drawn[index] with { Svg = null });
                }

                var pages = shown
                    .Select(page => shownPages.TryGetValue(pagePrefix + page.ToString(CultureInfo.InvariantCulture), out var entry) ? entry : default)
                    .Where(entry => entry.Marker is not null)
                    .ToList();
                var reused = shown.Except(toDraw).ToList();

                foreach (var (marker, _) in pages)
                {
                    AIInvocationScope.Current?.RequestFigureDisplay(marker);
                }

                response = BuildImageResponse(source, [.. pages.Select(page => page.Rendering)], [.. pages.Select(page => page.Marker)], omitted, pageCount, reused);
            }
        }

        if (missing.Count > 0)
        {
            response += Environment.NewLine + Environment.NewLine + string.Create(
                CultureInfo.InvariantCulture,
                $"The document has {pageCount} page(s), so page {PdfPageRange.Describe(missing)} does not exist and was not shown. If it should, change the document (for example add a page_break block) and preview again.");
        }

        Cache(key, response);

        return response;
    }

    private static string BuildImageResponse(
        PdfSource source,
        List<PdfPageRendering> renderings,
        List<string> markers,
        List<int> omitted,
        int pageCount,
        List<int> reused)
    {
        var builder = new StringBuilder();

        builder
            .AppendLine("WRITE THE FOLLOWING LINE IN YOUR ANSWER ONCE, EXACTLY AS SHOWN, ON A LINE OF ITS OWN:")
            .AppendLine()
            .AppendLine(string.Join(' ', markers))
            .AppendLine()
            .AppendLine("The markers are placeholders the host replaces with the page pictures; the user sees nothing unless they appear in your answer character for character. Do not describe them as \"shown above\" instead of writing them, and do not write them twice: each one becomes a picture every time it appears.");

        if (reused.Count > 0)
        {
            builder
                .AppendLine()
                .Append("Page ").Append(PdfPageRange.Describe(reused))
                .AppendLine(" was already shown in this turn and keeps the same marker; if your answer already has it, do not write it again.");
        }

        builder
            .AppendLine()
            .Append("What each picture shows (").Append(source.Describe()).AppendLine("), for your own wording only:");

        for (var index = 0; index < renderings.Count; index++)
        {
            var rendering = renderings[index];

            builder
                .Append(index + 1).Append(". Page ").Append(rendering.PageNumber).Append(" of ").Append(pageCount)
                .Append(" (").Append(PdfPageSizes.Describe(rendering.Width, rendering.Height)).Append(')');

            if (rendering.ImagesSkipped > 0)
            {
                builder.Append("; ").Append(rendering.ImagesSkipped).Append(" picture(s) drawn as placeholders to keep the preview small");
            }

            if (rendering.Simplified)
            {
                builder.Append("; dense vector artwork simplified");
            }

            builder.AppendLine(".");
        }

        if (omitted.Count > 0)
        {
            builder.Append("Not shown: pages ").Append(PdfPageRange.Describe(omitted)).AppendLine(". Say so, and call preview_pdf with 'pages' to show them.");
        }
        else if (pageCount > renderings.Count)
        {
            builder.Append("The document has ").Append(pageCount).AppendLine(" pages; pass 'pages' to show others.");
        }

        builder.Append("The preview is drawn from the same file export_pdf writes; it is not a download.");

        return builder.ToString();
    }

    private static string BuildTextResponse(
        UglyToad.PdfPig.PdfDocument pdf,
        PdfSource source,
        List<int> shown,
        List<int> omitted,
        int pageCount,
        string reason)
    {
        var builder = new StringBuilder();

        builder.Append(reason).Append(' ').Append("Text of ").Append(source.Describe()).Append(", ").Append(pageCount).AppendLine(" page(s):");

        foreach (var number in shown)
        {
            var text = PdfPageText.GetText(pdf.GetPage(number));

            builder.AppendLine().Append("--- Page ").Append(number).AppendLine(" ---");
            builder.AppendLine(string.IsNullOrWhiteSpace(text) ? "(no text on this page)" : text);
        }

        if (omitted.Count > 0)
        {
            builder.AppendLine().Append("Not shown: pages ").Append(PdfPageRange.Describe(omitted)).Append('.');
        }

        return builder.ToString();
    }

    private static Dictionary<string, (string Marker, PdfPageRendering Rendering)> ShownPages()
    {
        var invocation = AIInvocationScope.Current;

        if (invocation is null)
        {
            return [];
        }

        if (!invocation.Items.TryGetValue(ShownPagesKey, out var value) || value is not Dictionary<string, (string Marker, PdfPageRendering Rendering)> pages)
        {
            pages = new Dictionary<string, (string Marker, PdfPageRendering Rendering)>(StringComparer.Ordinal);
            invocation.Items[ShownPagesKey] = pages;
        }

        return pages;
    }

    private static bool TryGetCached(string key, out string response)
    {
        response = null;

        return AIInvocationScope.Current?.Items.TryGetValue(CacheKey, out var value) == true &&
            value is Dictionary<string, string> cache &&
            cache.TryGetValue(key, out response);
    }

    private static void Cache(string key, string response)
    {
        var invocation = AIInvocationScope.Current;

        if (invocation is null)
        {
            return;
        }

        if (!invocation.Items.TryGetValue(CacheKey, out var value) || value is not Dictionary<string, string> cache)
        {
            cache = new Dictionary<string, string>(StringComparer.Ordinal);
            invocation.Items[CacheKey] = cache;
        }

        cache[key] = response;
    }
}
