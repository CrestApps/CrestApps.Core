using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Lists the links in a PDF — web addresses, email links, jumps to other pages, named destinations and
/// actions — with the text they sit on and where they lead, and optionally checks them without following
/// any.
/// </summary>
internal sealed class ExtractPdfLinksTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ExtractPdfLinks;

    private const int MaxNamedDestinationsShown = 40;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "validate": {
              "type": "boolean",
              "description": "Check each link: that addresses are well formed and use a safe scheme (javascript:, file: and data: are flagged as risky, as is text that shows one address while linking to another), and that internal links lead to a page that exists. No link is ever opened, so whether a web page answers is not checked. Defaults to false."
            }
          },
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractPdfLinksTool"/> class.
    /// </summary>
    public ExtractPdfLinksTool()
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
    public override string Description => "Lists the links in a PDF with their page, the text they sit on, their box and their target: web and email addresses, jumps to other pages (with the page number), links to other files, scripts and viewer actions, plus the document's named destinations. With validate: true it checks addresses for being well formed and safe and internal links for pointing at pages that exist, without opening anything (reachability is not tested).";

    /// <summary>
    /// Lists the links.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The conversation's PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The links.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var validate = arguments.GetBoolean("validate") ?? false;

        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        using var pdf = PdfFiles.OpenForReading(bytes, arguments.GetString("password"));
        var pages = PdfPageRange.Parse(arguments.GetPages(), pdf.NumberOfPages);

        var reader = new PdfLinkReader(pdf);
        var lines = new List<string>();
        var links = new List<PdfLinkEntry>();

        foreach (var number in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = pdf.GetPage(number);
            var visible = PdfBox.VisibleArea(page);
            var pageLinks = reader.Read(page, PdfPageText.GetWords(page));

            foreach (var link in pageLinks)
            {
                if (validate)
                {
                    PdfLinkReader.Validate(link, pdf.NumberOfPages, visible);
                }

                links.Add(link);
                lines.Add(Describe(links.Count, link, link.Box.Describe(page), validate));
            }
        }

        var named = reader.ReadNamedDestinations();
        var scope = PdfPageSelection.Describe(pages, pdf.NumberOfPages);
        var writer = new PdfResponseWriter(context.Options.MaxToolResponseCharacters);

        if (links.Count == 0)
        {
            writer.Line($"No links found in \"{source.Name}\" ({scope}).");
        }
        else
        {
            var kinds = links.GroupBy(link => link.Kind).Select(group => FormattableString.Invariant($"{group.Count()} {group.Key}"));

            writer.Line(FormattableString.Invariant($"{links.Count} link(s) in \"{source.Name}\" ({scope}): {string.Join(", ", kinds)}. Boxes are x, y, w, h in points from the page's top-left corner."));

            if (validate)
            {
                var problems = links.Count(link => link.Status is not "ok");

                writer.Line(problems == 0
                    ? "Every link checked out."
                    : FormattableString.Invariant($"{problems} link(s) need attention (status other than ok)."));
            }

            writer.Line();

            var written = 0;

            foreach (var line in lines)
            {
                if (!writer.TryLine(line))
                {
                    break;
                }

                written++;
            }

            if (written < lines.Count)
            {
                writer.Line(FormattableString.Invariant($"[Listed {written} of {lines.Count} links to stay within the answer size; narrow 'pages' to see the rest.]"));
            }
        }

        if (named.Count > 0)
        {
            var entries = named
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Take(MaxNamedDestinationsShown)
                .Select(entry => entry.Value is int page
                    ? FormattableString.Invariant($"{entry.Key} → page {page}")
                    : entry.Key + " → (does not resolve)");

            writer.Line();
            writer.Line(FormattableString.Invariant($"Named destinations ({named.Count}): ") + string.Join(", ", entries) + (named.Count > MaxNamedDestinationsShown ? ", …" : string.Empty));
        }

        if (validate)
        {
            writer.Line();
            writer.Line("No link was opened: web addresses were checked for form and safety only, so whether each page still answers is not known.");
        }

        return writer.ToString();
    }

    private static string Describe(int index, PdfLinkEntry link, string box, bool validate)
    {
        var text = link.DescribeText();
        var target = link.Target ?? string.Empty;

        if (link.TargetPage is int page)
        {
            target = string.IsNullOrEmpty(target)
                ? "page " + page.ToString(CultureInfo.InvariantCulture)
                : target + " (page " + page.ToString(CultureInfo.InvariantCulture) + ")";
        }

        var line = FormattableString.Invariant($"{index}. p{link.Page} {link.Kind}: {text} → {(string.IsNullOrEmpty(target) ? "(no target)" : target)} [{box}]");

        if (validate)
        {
            line += " — " + link.Status + (string.IsNullOrEmpty(link.Note) ? string.Empty : ": " + link.Note);
        }

        return line;
    }
}
